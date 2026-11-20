using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

public class AccountController : Controller
{
    private const string Scheme = "KaonCookie";
    private readonly KaonDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly IEmailSender _email;
    private readonly StoreContext _stores;

    public AccountController(KaonDbContext db, IPasswordHasher<AppUser> hasher, IEmailSender email, StoreContext stores)
    {
        _db = db;
        _hasher = hasher;
        _email = email;
        _stores = stores;
    }

    // The carinderia whose link or QR code this visitor opened before signing in or signing up
    private async Task<StoreSettings?> LinkedStore()
    {
        var sid = HttpContext.Session.GetInt32("storeId");
        if (sid == null) return null;
        return await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid);
    }

    private async Task ShowLinkedStore() => ViewBag.JoinStoreName = (await LinkedStore())?.Name;

    private IActionResult GoHome(string? role = null)
    {
        role ??= User.FindFirstValue(ClaimTypes.Role);
        return role switch
        {
            "Admin" => RedirectToAction("Index", "Admin"),
            "Owner" => RedirectToAction("Index", "Owner"),
            _ => RedirectToAction("Index", "Menu")
        };
    }

    private Task SignInUser(AppUser user) => Helpers.SignIn.UserAsync(HttpContext, user);

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Login()
    {
        if (User.Identity?.IsAuthenticated == true) return GoHome();
        await ShowLinkedStore();
        return View(new LoginForm());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form)
    {
        await ShowLinkedStore();
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();

        // Customers belong to one carinderia each. Arriving from a carinderia's link, only that
        // carinderia's customers (and staff) are considered. Without a link, any account with this email.
        var linked = await LinkedStore();
        var candidates = await _db.Users
            .Where(u => u.Email == email && u.Role != "Admin" &&
                        (u.Role != "Customer" || (linked != null && u.StoreId == linked.Id)))
            .ToListAsync();

        // Too many wrong passwords: the account rests for a few minutes
        if (candidates.Any(c => c.LockoutUntil != null && c.LockoutUntil > DateTime.UtcNow))
        {
            ModelState.AddModelError("", "Too many tries. Please wait a few minutes and try again, or reset your password.");
            return View(form);
        }

        var matches = candidates
            .Where(u => !string.IsNullOrEmpty(u.PasswordHash) &&
                        _hasher.VerifyHashedPassword(u, u.PasswordHash, form.Password) != PasswordVerificationResult.Failed)
            .ToList();

        if (matches.Count == 0)
        {
            foreach (var u in candidates)
            {
                u.FailedLogins++;
                if (u.FailedLogins >= 5)
                {
                    u.LockoutUntil = DateTime.UtcNow.AddMinutes(10);
                    u.FailedLogins = 0;
                }
            }
            if (candidates.Count > 0) await _db.SaveChangesAsync();
            ModelState.AddModelError("", linked == null
                ? "Wrong email or password. Customers: open your carinderia's link or scan its QR code to log in."
                : "Wrong email or password. Accounts are separate for each carinderia: if you signed up at another one, open its link instead.");
            return View(form);
        }

        if (matches.Count > 1)
        {
            ModelState.AddModelError("", "This email has accounts at more than one carinderia. Please open your carinderia's link or scan its QR code, then log in there.");
            return View(form);
        }

        var user = matches[0];
        if (user.FailedLogins != 0 || user.LockoutUntil != null)
        {
            user.FailedLogins = 0;
            user.LockoutUntil = null;
            await _db.SaveChangesAsync();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError("", "This account is turned off. Please contact the Kaon team.");
            return View(form);
        }

        await SignInUser(user);
        return user.MustChangePassword
            ? RedirectToAction(nameof(ChangePassword))
            : GoHome(user.Role);
    }

    // "Forgot password": we email a one-time link. The answer is always the same, so nobody can
    // use this page to find out which emails have accounts.
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        var linked = await LinkedStore();
        // One email per account (the same email can be a customer at several carinderias)
        var users = await _db.Users
            .Where(u => u.Email == email && u.IsActive && u.PasswordHash != "" &&
                        (u.Role != "Customer" || (linked != null && u.StoreId == linked.Id)))
            .ToListAsync();
        var storeNames = await _db.StoreSettings.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);

        foreach (var user in users)
        {
            // Not more than one email every 2 minutes per account
            var recent = await _db.PasswordTokens.AnyAsync(t =>
                t.UserId == user.Id && t.Purpose == "Reset" && t.CreatedAt > DateTime.UtcNow.AddMinutes(-2));
            if (recent) continue;

            var old = await _db.PasswordTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync();
            foreach (var o in old) o.UsedAt = DateTime.UtcNow;

            var (token, hash) = InviteTokens.New();
            _db.PasswordTokens.Add(new PasswordToken
            {
                UserId = user.Id, TokenHash = hash, Purpose = "Reset",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await _db.SaveChangesAsync();

            var link = Url.Action(nameof(SetPassword), "Account", new { token }, Request.Scheme)!;
            var who = user.Role == "Customer" && user.StoreId is int sid && storeNames.TryGetValue(sid, out var sn)
                ? user.FullName + " (" + sn + ")" : user.FullName;
            await _email.SendAsync(user.Email, "Reset your Kaon password", EmailTemplates.Reset(who, link, 60));
        }

        ViewBag.Sent = true;
        return View(new ForgotPasswordForm());
    }

    // The link the Kaon team sends: lets the person choose their OWN password. The admin never sees it.
    private async Task<PasswordToken?> FindToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = InviteTokens.Hash(token);
        return await _db.PasswordTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null &&
                                      t.ExpiresAt > DateTime.UtcNow && t.User!.IsActive);
    }

    [HttpGet]
    public async Task<IActionResult> SetPassword(string? token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Cache-Control"] = "no-store";
        var t = await FindToken(token);
        if (t == null) return View("SetPasswordInvalid");
        ViewBag.Name = t.User!.FullName;
        ViewBag.Purpose = t.Purpose;
        return View(new SetPasswordForm { Token = token! });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(SetPasswordForm form)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Cache-Control"] = "no-store";
        var t = await FindToken(form.Token);
        if (t == null) return View("SetPasswordInvalid");

        if (!ModelState.IsValid)
        {
            ViewBag.Name = t.User!.FullName;
            ViewBag.Purpose = t.Purpose;
            return View(form);
        }

        var user = t.User!;
        user.PasswordHash = _hasher.HashPassword(user, form.NewPassword);
        user.MustChangePassword = false;

        // The link works once. Any other link for this person stops working too.
        t.UsedAt = DateTime.UtcNow;
        var others = await _db.PasswordTokens.Where(x => x.UserId == user.Id && x.UsedAt == null && x.Id != t.Id).ToListAsync();
        foreach (var o in others) o.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Saved"] = "Your password is set. You can log in now.";
        return user.Role == "Admin" ? Redirect("/admin/login") : RedirectToAction(nameof(Login));
    }

    // My account: everyone sees and edits their own details here, and finds their password settings
    [HttpGet, Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Profile()
    {
        var me = await Me();
        if (me == null) return RedirectToAction(nameof(Login));
        await FillProfile(me);
        return View(new ProfileForm { FullName = me.FullName, Phone = me.Phone });
    }

    [HttpPost, Microsoft.AspNetCore.Authorization.Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileForm form)
    {
        var me = await Me();
        if (me == null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) { await FillProfile(me); return View(form); }

        me.FullName = form.FullName.Trim();
        me.Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim();
        await _db.SaveChangesAsync();
        await SignInUser(me);   // the new name shows in the menu right away
        TempData["Saved"] = "Your details are saved.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<AppUser?> Me()
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    private async Task FillProfile(AppUser me)
    {
        ViewBag.Email = me.Email;
        ViewBag.Role = me.Role switch { "Admin" => "Kaon admin", "Owner" => "Carinderia owner", _ => "Customer" };
        ViewBag.StoreName = me.StoreId is int sid
            ? await _db.StoreSettings.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).FirstOrDefaultAsync()
            : null;
    }

    // Everyone can change their own password. A temporary password must be changed here first.
    [HttpGet, Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordForm());

    [HttpPost, Microsoft.AspNetCore.Authorization.Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return RedirectToAction(nameof(Login));

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, form.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(form.CurrentPassword), "That is not your current password.");
            return View(form);
        }
        if (form.NewPassword == form.CurrentPassword)
        {
            ModelState.AddModelError(nameof(form.NewPassword), "Choose a different password from the one you have now.");
            return View(form);
        }

        user.PasswordHash = _hasher.HashPassword(user, form.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();

        await SignInUser(user);   // refresh the sign-in so the "must change" flag is gone
        TempData["Saved"] = "Password changed.";
        return GoHome(user.Role);
    }

    [HttpGet]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true) return GoHome();
        // Customers sign up at a carinderia, from its link or QR code
        if (await LinkedStore() == null) return View("NoLink");
        await ShowLinkedStore();
        return View(new RegisterForm());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        var linked = await LinkedStore();
        if (linked == null) return View("NoLink");
        await ShowLinkedStore();
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        // Customers are separate at each carinderia, so only staff emails and this carinderia's customers count
        if (await _db.Users.AnyAsync(u => u.Email == email && (u.Role != "Customer" || u.StoreId == linked.Id)))
        {
            ModelState.AddModelError(nameof(form.Email), "That email is already registered here. Please log in instead.");
            return View(form);
        }

        var user = new AppUser
        {
            FullName = form.FullName.Trim(),
            Phone = form.Phone.Trim(),
            Email = email,
            Role = "Customer",
            StoreId = linked.Id
        };
        user.PasswordHash = _hasher.HashPassword(user, form.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SignInUser(user);
        return GoHome(user.Role);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var wasAdmin = User.IsInRole("Admin");
        await HttpContext.SignOutAsync(Scheme);
        HttpContext.Session.Clear();
        return wasAdmin ? Redirect("/admin/login") : RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => GoHome();
}