using System.Collections.Concurrent;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// The public sign-up for carinderia owners. No approval is needed: the owner creates the account,
// is signed in, and has their own carinderia with its link and QR code, and the free trial month.
public class ApplyController : Controller
{
    private const int PerIpPerHour = 5;
    private const int NewCarinderiasPerDay = 40;
    private static readonly ConcurrentDictionary<string, List<DateTime>> Recent = new();

    private readonly KaonDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly IEmailSender _email;
    private readonly ILogger<ApplyController> _log;

    public ApplyController(KaonDbContext db, IPasswordHasher<AppUser> hasher, IEmailSender email, ILogger<ApplyController> log)
    {
        _db = db;
        _hasher = hasher;
        _email = email;
        _log = log;
    }

    [HttpGet("/for-carinderias")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Owner")) return RedirectToAction("Index", "Owner");
        ViewBag.Plan = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PlatformSettings();
        return View(new OwnerSignupForm());
    }

    [HttpPost("/for-carinderias"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OwnerSignupForm form)
    {
        ViewBag.Plan = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PlatformSettings();

        // A bot filled the hidden field: pretend it worked
        if (!string.IsNullOrEmpty(form.Website)) return RedirectToAction("Index", "Home");
        if (!ModelState.IsValid) return View(form);

        // Without an approval step, keep the sign-up from being flooded
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (TooMany(ip) || await _db.Subscriptions.CountAsync(s => s.CreatedAt > DateTime.UtcNow.AddDays(-1)) >= NewCarinderiasPerDay)
        {
            ModelState.AddModelError("", "Too many sign-ups right now. Please try again later.");
            return View(form);
        }

        var email = form.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email && u.Role != "Customer"))
        {
            ModelState.AddModelError(nameof(form.Email), "That email already has a Kaon owner account. Please log in instead.");
            return View(form);
        }

        var (store, owner) = await Onboarding.CreateAsync(_db, _hasher, form.CarinderiaName, form.OwnerName, email, form.Phone, form.Password);
        Remember(ip);

        // Automatic messages (sent only when email is set up)
        var loginUrl = Url.Action("Login", "Account", null, Request.Scheme)!;
        await Notify.ToAsync(_email, _log, owner.Email, "Welcome to Kaon", "Welcome to Kaon",
            store.Name + " is ready. Log in at " + loginUrl + " to add your dishes and share your QR code. Your first month is free.");
        await Notify.AdminsAsync(_db, _email, _log, "A new carinderia joined Kaon", "A new carinderia signed up",
            store.Name + " (" + owner.FullName + ") created an account.");

        // Signed in straight away: the owner lands in their own dashboard
        await Helpers.SignIn.UserAsync(HttpContext, owner);
        TempData["Saved"] = "Welcome to Kaon! " + store.Name + " is ready. Add your dishes, then share your link and QR code.";
        return RedirectToAction("Index", "Owner");
    }

    private static bool TooMany(string ip)
    {
        var list = Recent.GetOrAdd(ip, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(t => t < DateTime.UtcNow.AddHours(-1));
            return list.Count >= PerIpPerHour;
        }
    }

    private static void Remember(string ip)
    {
        var list = Recent.GetOrAdd(ip, _ => new List<DateTime>());
        lock (list) list.Add(DateTime.UtcNow);
    }
}
