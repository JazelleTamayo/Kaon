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

    private async Task SignInUser(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.MustChangePassword) claims.Add(new Claim("must_change", "1"));
        var identity = new ClaimsIdentity(claims, Scheme);
        await HttpContext.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

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
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Too many wrong passwords: the account rests for a few minutes
        if (user?.LockoutUntil != null && user.LockoutUntil > DateTime.UtcNow)
        {
            ModelState.AddModelError("", "Too many tries. Please wait a few minutes and try again, or reset your password.");
            return View(form);
        }

        if (user == null || string.IsNullOrEmpty(user.PasswordHash) ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, form.Password) == PasswordVerificationResult.Failed)
        {
            if (user != null)
            {
                user.FailedLogins++;
                if (user.FailedLogins >= 5)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(10);
                    user.FailedLogins = 0;
                }
                await _db.SaveChangesAsync();
            }
            ModelState.AddModelError("", "Wrong email or password.");
            return View(form);
        }

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

        // A customer who opened a carinderia's link first is now one of that carinderia's customers
        if (user.Role == "Customer" && await LinkedStore() is { } linked)
            await _stores.JoinAsync(user.Id, linked.Id);

        // A customer who did not come from a carinderia's link, and has joined more than one,
        // chooses where to order now instead of being sent to a "default" one
        var chooseStore = user.Role == "Customer" && await LinkedStore() == null &&
                          await _db.CustomerStores.CountAsync(c => c.UserId == user.Id) > 1;
        if (chooseStore) HttpContext.Session.Remove("storeId");

        await SignInUser(user);
        return user.MustChangePassword
            ? RedirectToAction(nameof(ChangePassword))
            : chooseStore ? RedirectToAction("Mine", "Store") : GoHome(user.Role);
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
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null && user.IsActive && user.PasswordHash != "")
        {
            // Not more than one email every 2 minutes per account
            var recent = await _db.PasswordTokens.AnyAsync(t =>
                t.UserId == user.Id && t.Purpose == "Reset" && t.CreatedAt > DateTime.UtcNow.AddMinutes(-2));
            if (!recent)
            {
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
                await _email.SendAsync(user.Email, "Reset your Kaon password", EmailTemplates.Reset(user.FullName, link, 60));
            }
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
        return RedirectToAction(nameof(Login));
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
        await ShowLinkedStore();
        return View(new RegisterForm());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        await ShowLinkedStore();
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(nameof(form.Email), "That email is already registered.");
            return View(form);
        }

        var user = new AppUser
        {
            FullName = form.FullName.Trim(),
            Phone = form.Phone.Trim(),
            Email = email,
            Role = "Customer",
            JoinedStoreId = (await LinkedStore())?.Id
        };
        user.PasswordHash = _hasher.HashPassword(user, form.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        if (user.JoinedStoreId is int joined) await _stores.JoinAsync(user.Id, joined);

        await SignInUser(user);
        return GoHome(user.Role);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(Scheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => GoHome();
}