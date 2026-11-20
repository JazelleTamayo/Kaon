using Kaon.Data;
using Kaon.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Every carinderia is its own place with its own link (and QR code): /c/jamaica-foods.
// Customers belong to the carinderia they sign up at. There is NO list of carinderias anywhere.
public class StoreController : Controller
{
    private readonly KaonDbContext _db;
    private readonly StoreContext _stores;

    public StoreController(KaonDbContext db, StoreContext stores)
    {
        _db = db;
        _stores = stores;
    }

    // The carinderia's own front door
    [HttpGet("/c/{slug}")]
    public async Task<IActionResult> Landing(string slug)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        var store = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug);
        if (store == null) return NotFound();

        if (User.Identity?.IsAuthenticated == true)
        {
            // A signed-in customer of THIS carinderia goes straight to its menu
            if (User.IsInRole("Customer") && await _stores.IdAsync() == store.Id)
                return RedirectToAction("Index", "Menu");

            // A customer of another carinderia: the page explains and offers to log out
            if (User.IsInRole("Customer"))
                ViewBag.OtherStore = (await _stores.StoreAsync()).Name;

            // An owner or the admin: show what customers see (look only), in the same browser
            if (User.IsInRole("Owner") || User.IsInRole("Admin"))
            {
                var shown = await StoreGate.ForCustomersAsync(_db, store.Id);
                var today = DateTime.Today;
                var entries = await _db.DailyMenuEntries.AsNoTracking().Include(e => e.MenuItem)
                    .Where(e => e.Date == today && e.MenuItem!.StoreId == store.Id).ToListAsync();
                var packages = await _db.MenuItems.AsNoTracking().Include(m => m.Options)
                    .Where(m => m.StoreId == store.Id && m.Category == MenuItem.PackageCategory).ToListAsync();
                return View("Preview", new StorePreviewVm
                {
                    Store = shown,
                    Entries = entries.OrderBy(e => Array.IndexOf(MenuSeries.All, e.MenuItem!.SeriesName)).ThenBy(e => e.MenuItem!.Name).ToList(),
                    Packages = packages.OrderBy(m => m.Name).ToList()
                });
            }
        }
        else
        {
            _stores.Remember(store.Id);   // the next sign-in or sign-up is for this carinderia
        }

        return View(store);
    }

    // A customer account that belongs to no carinderia yet (older accounts only)
    [Authorize(Roles = "Customer")]
    public IActionResult NoStore() => View();
}
