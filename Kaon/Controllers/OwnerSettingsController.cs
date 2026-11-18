using Kaon.Data;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 2: Carinderia store setup
[Authorize(Roles = "Owner")]
public class OwnerSettingsController : Controller
{
    private readonly KaonDbContext _db;
    public OwnerSettingsController(KaonDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _db.StoreSettings.AsNoTracking().FirstAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(StoreSettings form)
    {
        if (!form.PickupEnabled && !form.DeliveryEnabled)
            ModelState.AddModelError("", "Turn on at least one: pickup or home delivery.");
        if (!ModelState.IsValid) return View(form);

        var s = await _db.StoreSettings.FirstAsync();
        s.Name = form.Name.Trim();
        s.Hours = form.Hours?.Trim();
        s.Phone = form.Phone?.Trim();
        s.DeliveryArea = form.DeliveryArea?.Trim();
        s.DeliveryFee = form.DeliveryFee;
        s.PickupEnabled = form.PickupEnabled;
        s.DeliveryEnabled = form.DeliveryEnabled;
        s.IsOpen = form.IsOpen;
        await _db.SaveChangesAsync();

        TempData["Saved"] = "Store settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
