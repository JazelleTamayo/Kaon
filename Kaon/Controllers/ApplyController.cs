using Kaon.Data;
using Kaon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// The public "For carinderias" page: a carinderia owner asks to join Kaon.
// The request is saved for the Kaon admin. No account is created here.
public class ApplyController : Controller
{
    private readonly KaonDbContext _db;
    public ApplyController(KaonDbContext db) => _db = db;

    [HttpGet("/for-carinderias")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Plan = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PlatformSettings();
        return View(new ApplyForm());
    }

    [HttpPost("/for-carinderias"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ApplyForm form)
    {
        // A bot filled the hidden field: pretend it worked
        if (!string.IsNullOrEmpty(form.Website)) return RedirectToAction(nameof(Thanks));
        ViewBag.Plan = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PlatformSettings();
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(nameof(form.Email), "That email already has a Kaon account. Please log in instead.");
            return View(form);
        }

        // One waiting request per email, and a limit per day, so the form can't be used to flood the admin
        var since = DateTime.UtcNow.AddDays(-1);
        if (await _db.CarinderiaRequests.AnyAsync(r => r.Email == email && r.Status == "New") ||
            await _db.CarinderiaRequests.CountAsync(r => r.CreatedAt > since) >= 50)
            return RedirectToAction(nameof(Thanks));

        _db.CarinderiaRequests.Add(new CarinderiaRequest
        {
            CarinderiaName = form.CarinderiaName.Trim(),
            OwnerName = form.OwnerName.Trim(),
            Phone = form.Phone.Trim(),
            Email = email,
            Address = form.Address.Trim(),
            Message = string.IsNullOrWhiteSpace(form.Message) ? null : form.Message.Trim()
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Thanks));
    }

    [HttpGet("/for-carinderias/thanks")]
    public IActionResult Thanks() => View();
}
