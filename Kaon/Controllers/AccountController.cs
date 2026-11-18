using System.Security.Claims;
using Kaon.Data;
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

    public AccountController(KaonDbContext db, IPasswordHasher<AppUser> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    private IActionResult GoHome(string? role = null)
    {
        role ??= User.FindFirstValue(ClaimTypes.Role);
        return role == "Owner"
            ? RedirectToAction("Index", "Owner")
            : RedirectToAction("Index", "Menu");
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
        var identity = new ClaimsIdentity(claims, Scheme);
        await HttpContext.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true) return GoHome();
        return View(new LoginForm());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, form.Password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Wrong email or password.");
            return View(form);
        }

        await SignInUser(user);
        return GoHome(user.Role);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return GoHome();
        return View(new RegisterForm());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm form)
    {
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
            Role = "Customer"
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
        await HttpContext.SignOutAsync(Scheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => GoHome();
}