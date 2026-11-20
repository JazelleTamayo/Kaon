using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// The Kaon team's own login page. Only Kaon admin accounts can sign in here, and admins can
// not sign in on the shared login page. It is not linked from any public page.
public class AdminLoginController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;

    public AdminLoginController(KaonDbContext db, IPasswordHasher<AppUser> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [HttpGet("/admin/login")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            return RedirectToAction("Index", "Admin");
        return View(new LoginForm());
    }

    [HttpPost("/admin/login")]
    public async Task<IActionResult> Index(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        var admins = await _db.Users.Where(u => u.Email == email && u.Role == "Admin").ToListAsync();

        // Too many wrong passwords: the account rests for a few minutes
        if (admins.Any(a => a.LockoutUntil != null && a.LockoutUntil > DateTime.UtcNow))
        {
            ModelState.AddModelError("", "Too many tries. Please wait a few minutes and try again, or reset your password.");
            return View(form);
        }

        var admin = admins.FirstOrDefault(a => !string.IsNullOrEmpty(a.PasswordHash) &&
            _hasher.VerifyHashedPassword(a, a.PasswordHash, form.Password) != PasswordVerificationResult.Failed);

        if (admin == null)
        {
            foreach (var a in admins)
            {
                a.FailedLogins++;
                if (a.FailedLogins >= 5) { a.LockoutUntil = DateTime.UtcNow.AddMinutes(10); a.FailedLogins = 0; }
            }
            if (admins.Count > 0) await _db.SaveChangesAsync();
            ModelState.AddModelError("", "Wrong email or password.");
            return View(form);
        }

        if (admin.FailedLogins != 0 || admin.LockoutUntil != null)
        {
            admin.FailedLogins = 0;
            admin.LockoutUntil = null;
            await _db.SaveChangesAsync();
        }
        if (!admin.IsActive)
        {
            ModelState.AddModelError("", "This account is turned off.");
            return View(form);
        }

        await Helpers.SignIn.UserAsync(HttpContext, admin);
        return admin.MustChangePassword
            ? RedirectToAction("ChangePassword", "Account")
            : RedirectToAction("Index", "Admin");
    }
}
