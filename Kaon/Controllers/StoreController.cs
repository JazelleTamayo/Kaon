using Kaon.Data;
using Kaon.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Every carinderia is its own place with its own link (and QR code): /c/jamaica-foods.
// There is NO list of carinderias anywhere: a customer reaches one only through its own link or QR.
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

        if (User.Identity?.IsAuthenticated != true || User.IsInRole("Customer"))
        {
            _stores.Remember(store.Id);   // the next sign-in or sign-up is for this carinderia
            if (User.IsInRole("Customer") && _stores.UserId is int uid)
            {
                await _stores.JoinAsync(uid, store.Id);
                return RedirectToAction("Index", "Menu");
            }
        }

        return View(store);
    }

    // A signed-in customer who has not opened any carinderia's link yet
    [Authorize(Roles = "Customer")]
    public IActionResult NoStore() => View();

    // The carinderias this customer has joined (only theirs; there is no public list)
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Mine()
    {
        var uid = _stores.UserId!.Value;
        var joined = await _db.CustomerStores.AsNoTracking()
            .Where(c => c.UserId == uid)
            .Join(_db.StoreSettings.AsNoTracking(), c => c.StoreId, s => s.Id, (c, s) => new { s, c.JoinedAt })
            .OrderBy(x => x.s.Name).ToListAsync();
        ViewBag.CurrentId = await _stores.IdAsync();
        return View(joined.Select(x => x.s).ToList());
    }

    [HttpPost, Authorize(Roles = "Customer"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(int storeId)
    {
        var uid = _stores.UserId!.Value;
        // Only among the carinderias this customer joined
        if (!await _db.CustomerStores.AnyAsync(c => c.UserId == uid && c.StoreId == storeId))
            return NotFound();
        _stores.Remember(storeId);
        return RedirectToAction("Index", "Menu");
    }
}
