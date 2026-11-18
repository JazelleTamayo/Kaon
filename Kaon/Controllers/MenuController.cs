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
    public MenuController(KaonDbContext db) => _db = db;

    // The daily menu: everyday meals
    public async Task<IActionResult> Index()
    {
        ViewBag.Store = await _db.StoreSettings.AsNoTracking().FirstAsync();
        ViewBag.PackageCount = await _db.MenuItems.CountAsync(m => m.Category == MenuItem.PackageCategory && m.IsAvailable);
        var items = await _db.MenuItems
            .Where(m => m.Category != MenuItem.PackageCategory)
            .OrderBy(m => m.Name)
            .ToListAsync();
        return View(items.OrderBy(m => Array.IndexOf(MenuSeries.All, m.SeriesName)).ThenBy(m => m.Name).ToList());
    }

    // Food packages: party trays and lechon, with sizes
    public async Task<IActionResult> Packages()
    {
        ViewBag.Store = await _db.StoreSettings.AsNoTracking().FirstAsync();
        var items = await _db.MenuItems
            .Include(m => m.Options)
            .Where(m => m.Category == MenuItem.PackageCategory)
            .ToListAsync();
        return View(items.OrderBy(m => Array.IndexOf(MenuSeries.All, m.SeriesName)).ThenBy(m => m.Name).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Add(int id, int? optionId, int qty = 1, string? returnTo = null)
    {
        var back = returnTo == "Packages" ? nameof(Packages) : nameof(Index);
        var store = await _db.StoreSettings.AsNoTracking().FirstAsync();
        var item = await _db.MenuItems.Include(m => m.Options).FirstOrDefaultAsync(m => m.Id == id);
        if (!store.IsOpen || item == null || !item.CanOrder)
            return RedirectToAction(back);

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

        // Limited-portion meals can't go over what is left
        var inCart = cart.First(c => c.MenuItemId == id && c.OptionId == option?.Id);
        if (item.Stock != null && inCart.Quantity > item.Stock) inCart.Quantity = item.Stock.Value;

        CartHelper.Save(HttpContext.Session, cart);
        TempData["Saved"] = item.Name + (option != null ? " (" + option.Label + ")" : "") + " added to your cart.";

        return Redirect(Url.Action(back) + "#item-" + id);
    }
}
