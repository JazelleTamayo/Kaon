using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

[Authorize(Roles = "Customer")]
public class MenuController : Controller
{
    private readonly KaonDbContext _db;
    private readonly StoreContext _stores;
    public MenuController(KaonDbContext db, StoreContext stores) { _db = db; _stores = stores; }

    // The daily menu: today's picks from the owner's dish catalog
    public async Task<IActionResult> Index()
    {
        var sid = await _stores.RequireAsync();
        var store = await StoreGate.ForCustomersAsync(_db, sid);
        ViewBag.Store = store;
        var today = DateTime.Today;
        var entries = store.OffersDailyMenu
            ? await _db.DailyMenuEntries.Include(e => e.MenuItem).Where(e => e.Date == today && e.MenuItem!.StoreId == sid).ToListAsync()
            : new List<DailyMenuEntry>();
        return View(entries.OrderBy(e => Array.IndexOf(MenuSeries.All, e.MenuItem!.SeriesName)).ThenBy(e => e.MenuItem!.Name).ToList());
    }

    // Food packages: party trays and lechon, with sizes
    public async Task<IActionResult> Packages()
    {
        var sid = await _stores.RequireAsync();
        var store = await StoreGate.ForCustomersAsync(_db, sid);
        if (!store.OffersPackages)
        {
            TempData["Warn"] = "Food packages aren't offered right now.";
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Store = store;
        var items = await _db.MenuItems
            .Include(m => m.Options)
            .Where(m => m.StoreId == sid && m.Category == MenuItem.PackageCategory)
            .ToListAsync();
        return View(items.OrderBy(m => Array.IndexOf(MenuSeries.All, m.SeriesName)).ThenBy(m => m.Name).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Add(int id, int? optionId, int qty = 1, string? returnTo = null)
    {
        var back = returnTo == "Packages" ? nameof(Packages) : nameof(Index);
        var sid = await _stores.RequireAsync();
        var store = await StoreGate.ForCustomersAsync(_db, sid);
        // Only a dish of THIS carinderia can go in the cart
        var item = await _db.MenuItems.Include(m => m.Options).FirstOrDefaultAsync(m => m.Id == id && m.StoreId == sid);
        if (!store.IsOpen || item == null)
            return RedirectToAction(back);

        // Daily meals: only orderable if the owner picked them for today's menu.
        // Food packages keep using their own on/off switch.
        DailyMenuEntry? entry = null;
        if (!item.IsPackage)
        {
            entry = await _db.DailyMenuEntries.FirstOrDefaultAsync(e => e.MenuItemId == id && e.Date == DateTime.Today);
            if (entry == null || !entry.CanOrder) return RedirectToAction(back);
        }
        else if (!item.CanOrder)
        {
            return RedirectToAction(back);
        }

        // A whole section can be switched off by the carinderia
        if (item.IsPackage ? !store.OffersPackages : !store.OffersDailyMenu)
        {
            TempData["Warn"] = item.IsPackage ? "Food packages aren't offered right now." : "The daily menu isn't offered right now.";
            return RedirectToAction(nameof(Index));
        }

        qty = Math.Clamp(qty, 1, 100);

        ItemOption? option = null;
        if (item.Options.Any())
        {
            option = item.Options.FirstOrDefault(o => o.Id == optionId);
            if (option == null)
            {
                TempData["Warn"] = "Please choose a size for " + item.Name + ".";
                return RedirectToAction(back);
            }
        }

        var cart = CartHelper.Get(HttpContext.Session);
        var line = cart.FirstOrDefault(c => c.MenuItemId == id && c.OptionId == option?.Id);
        if (line == null)
        {
            cart.Add(new CartLine
            {
                MenuItemId = item.Id,
                Name = item.Name,
                Price = option?.Price ?? item.Price,
                Quantity = qty,
                OptionId = option?.Id,
                OptionLabel = option?.Label,
                IsPackage = item.IsPackage
            });
        }
        else
        {
            line.Quantity += qty;
        }

        // Limited-portion meals can't go over what is left today
        var inCart = cart.First(c => c.MenuItemId == id && c.OptionId == option?.Id);
        if (entry?.Stock != null && inCart.Quantity > entry.Stock) inCart.Quantity = entry.Stock.Value;

        CartHelper.Save(HttpContext.Session, cart);
        TempData["Saved"] = item.Name + (option != null ? " (" + option.Label + ")" : "") + " added to your cart.";

        return Redirect(Url.Action(back) + "#item-" + id);
    }
}
