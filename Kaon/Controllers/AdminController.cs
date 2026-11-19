using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 11: Kaon admin back-office. Only the Kaon team can open these pages.
//
// Privacy rules built into this controller:
//  - The admin NEVER sees or sets anyone's password. They send a one-time link and the
//    person chooses their own password.
//  - The admin cannot see customers' personal details, orders or payment proofs.
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private const int LinkHours = 48;
    private readonly KaonDbContext _db;
    private readonly IEmailSender _email;

    public AdminController(KaonDbContext db, IEmailSender email)
    {
        _db = db;
        _email = email;
    }

    private int MyId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        var orders = await _db.Orders.AsNoTracking().Select(o => o.CreatedAt).ToListAsync();
        var now = DateTime.UtcNow;

        var pending = await _db.PasswordTokens.AsNoTracking()
            .Where(t => t.UsedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Expires = g.Max(x => x.ExpiresAt) })
            .ToListAsync();

        return View(new AdminDashVm
        {
            Owners = await _db.Users.AsNoTracking().Where(u => u.Role == "Owner").OrderBy(u => u.FullName).ToListAsync(),
            CustomerCount = await _db.Users.CountAsync(u => u.Role == "Customer"),   // a number only
            OrdersToday = orders.Count(c => c >= today && c < today.AddDays(1)),
            OrdersTotal = orders.Count,
            Store = await _db.StoreSettings.AsNoTracking().FirstAsync(),
            PendingLinks = pending.ToDictionary(p => p.UserId, p => p.Expires)
        });
    }

    // Owners and admins only. Customers' details are private and are not listed here.
    public async Task<IActionResult> Users(string? role)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.Role == "Owner" || u.Role == "Admin");
        if (role is "Owner" or "Admin") query = query.Where(u => u.Role == role);
        ViewBag.Role = role ?? "";
        ViewBag.MyId = MyId;
        return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
    }

    [HttpGet]
    public IActionResult CreateOwner() => View(new CreateOwnerForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOwner(CreateOwnerForm form)
    {
        var email = form.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            ModelState.AddModelError(nameof(form.Email), "That email already has an account.");
        if (!ModelState.IsValid) return View(form);

        // The account starts with NO password. It cannot be used until the owner sets one.
        var owner = new AppUser
        {
            FullName = form.FullName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim(),
            Role = "Owner",
            PasswordHash = ""
        };
        _db.Users.Add(owner);
        await _db.SaveChangesAsync();

        var link = await IssueLink(owner, "Invite");
        TempData["InvMail"] = await _email.SendAsync(owner.Email, "Your Kaon account is ready",
            EmailTemplates.Invite(owner.FullName, link, LinkHours));
        ShowOnce("Invitation ready", owner.FullName, owner.Email, link, "Invite");
        return RedirectToAction(nameof(Invite));
    }

    // A new one-time link so the person can choose a new password themselves.
    // Their current password keeps working until they use the link.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendLink(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || user.Id == MyId || user.Role == "Admin") return RedirectToAction(nameof(Index));

        var purpose = user.PasswordHash == "" ? "Invite" : "Reset";
        var link = await IssueLink(user, purpose);
        TempData["InvMail"] = await _email.SendAsync(user.Email,
            purpose == "Invite" ? "Your Kaon account is ready" : "Reset your Kaon password",
            purpose == "Invite" ? EmailTemplates.Invite(user.FullName, link, LinkHours) : EmailTemplates.Reset(user.FullName, link, LinkHours * 60));
        ShowOnce(purpose == "Invite" ? "New invitation link" : "Password reset link", user.FullName, user.Email, link, purpose);
        return RedirectToAction(nameof(Invite));
    }

    // Turn an account off or back on. It is signed out on its next request.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, string? back)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null && user.Id != MyId && user.Role is "Owner")
        {
            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();
            TempData["Saved"] = user.FullName + (user.IsActive ? " is switched on." : " is switched off. They can't log in.");
        }
        return back == "users" ? RedirectToAction(nameof(Users)) : RedirectToAction(nameof(Index));
    }

    // Shown once, right after creating. A refresh does not show it again.
    public IActionResult Invite()
    {
        var title = TempData["InvTitle"] as string;
        var link = TempData["InvLink"] as string;
        if (title == null || link == null) return RedirectToAction(nameof(Index));

        ViewBag.Heading = title;
        ViewBag.Name = TempData["InvName"] as string;
        ViewBag.Email = TempData["InvEmail"] as string;
        ViewBag.Link = link;
        ViewBag.Purpose = TempData["InvPurpose"] as string;
        ViewBag.Mail = TempData["InvMail"] as string;
        ViewBag.Hours = LinkHours;
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }

    // A fresh link replaces any older one for that person
    private async Task<string> IssueLink(AppUser user, string purpose)
    {
        var old = await _db.PasswordTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync();
        foreach (var o in old) o.UsedAt = DateTime.UtcNow;

        var (token, hash) = InviteTokens.New();
        _db.PasswordTokens.Add(new PasswordToken
        {
            UserId = user.Id,
            TokenHash = hash,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddHours(LinkHours)
        });
        await _db.SaveChangesAsync();

        return Url.Action("SetPassword", "Account", new { token }, Request.Scheme)!;
    }

    private void ShowOnce(string title, string name, string email, string link, string purpose)
    {
        TempData["InvTitle"] = title;
        TempData["InvName"] = name;
        TempData["InvEmail"] = email;
        TempData["InvLink"] = link;
        TempData["InvPurpose"] = purpose;
    }
}
