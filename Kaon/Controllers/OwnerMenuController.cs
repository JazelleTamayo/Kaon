using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 3: Daily menu and food packages
[Authorize(Roles = "Owner")]
public class OwnerMenuController : Controller
{
    private static readonly string[] Categories = { "Meal", MenuItem.PackageCategory };
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly StoreContext _stores;
    public OwnerMenuController(KaonDbContext db, IWebHostEnvironment env, StoreContext stores)
    {
        _db = db;
        _env = env;
        _stores = stores;
    }

    public async Task<IActionResult> Index()
    {
        var sid = await _stores.RequireAsync();
        var today = DateTime.Today;
        var catalogMeals = await _db.MenuItems
            .Where(m => m.StoreId == sid && m.Category != MenuItem.PackageCategory)
            .OrderBy(m => m.Name)
            .ToListAsync();
        var todayEntries = await _db.DailyMenuEntries
            .Include(e => e.MenuItem)
            .Where(e => e.Date == today && e.MenuItem!.StoreId == sid)
            .ToListAsync();
        todayEntries = todayEntries.OrderBy(e => e.MenuItem!.Name).ToList();
        var packages = await _db.MenuItems
            .Include(m => m.Options)
            .Where(m => m.StoreId == sid && m.Category == MenuItem.PackageCategory)
            .OrderBy(m => m.Name)
            .ToListAsync();

        return View(new OwnerMenuVm
        {
            CatalogMeals = catalogMeals,
            TodayEntries = todayEntries,
            Packages = packages,
            Store = await _stores.StoreAsync(),
            LastMenuDate = await _db.DailyMenuEntries.Where(e => e.Date < today && e.MenuItem!.StoreId == sid).MaxAsync(e => (DateTime?)e.Date)
        });
    }

    private void FillForm()
    {
        ViewBag.Categories = Categories;
        ViewBag.SeriesList = MenuSeries.All;
    }

    [HttpGet]
    public IActionResult Create(bool package = false)
    {
        FillForm();
        return View("Edit", new MenuForm
        {
            Category = package ? MenuItem.PackageCategory : "Meal",
            Series = MenuSeries.Other,
            Options = package ? new List<OptionForm> { new() } : new()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var sid = await _stores.RequireAsync();
        var m = await _db.MenuItems.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id && x.StoreId == sid);
        if (m == null) return NotFound();
        FillForm();
        return View(new MenuForm
        {
            Id = m.Id, Name = m.Name, Description = m.Description, Price = m.Price,
            Category = m.Category, Series = m.SeriesName, IsAvailable = m.IsAvailable,
            ExistingImageUrl = m.ImageUrl,
            Options = m.Options.OrderBy(o => o.SortOrder)
                .Select(o => new OptionForm { Label = o.Label, Price = o.Price }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MenuForm form)
    {
        var isPackage = form.Category == MenuItem.PackageCategory;
        var options = form.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)).ToList();

        if (!Categories.Contains(form.Category))
            ModelState.AddModelError(nameof(form.Category), "Choose Meal or Food package.");
        if (!string.IsNullOrEmpty(form.Series) && !MenuSeries.All.Contains(form.Series))
            ModelState.AddModelError(nameof(form.Series), "Choose a series from the list.");

        if (isPackage)
        {
            if (options.Count == 0)
                ModelState.AddModelError("", "Add at least one size or portion with a price.");
            else if (options.Any(o => o.Price <= 0))
                ModelState.AddModelError("", "Every size needs a price above 0.");
        }
        else if (form.Price <= 0)
        {
            ModelState.AddModelError(nameof(form.Price), "Enter a price above 0.");
        }

        if (!ModelState.IsValid)
        {
            FillForm();
            if (isPackage && form.Options.Count == 0) form.Options.Add(new OptionForm());
            return View("Edit", form);
        }

        // Save the photo only now that everything else is valid
        string? newImageUrl = null;
        if (form.Image is { Length: > 0 })
        {
            var (url, error) = await MenuImageStorage.SaveAsync(form.Image, _env);
            if (error != null)
            {
                ModelState.AddModelError(nameof(form.Image), error);
                FillForm();
                if (isPackage && form.Options.Count == 0) form.Options.Add(new OptionForm());
                return View("Edit", form);
            }
            newImageUrl = url;
        }

        var sid = await _stores.RequireAsync();
        MenuItem? m = form.Id == 0
            ? new MenuItem { StoreId = sid }
            : await _db.MenuItems.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == form.Id && x.StoreId == sid);
        if (m == null) return NotFound();

        m.Name = form.Name.Trim();
        m.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        m.Category = form.Category;
        m.Series = string.IsNullOrEmpty(form.Series) ? MenuSeries.Other : form.Series;

        // Portions and "cooking today" for meals are managed per day on Today's menu, not here.
        // Food packages keep their own on/off switch exactly as before.
        m.Stock = null;
        m.IsAvailable = isPackage ? form.IsAvailable : true;

        if (newImageUrl != null)
        {
            MenuImageStorage.DeleteIfUploaded(_env, m.ImageUrl);
            m.ImageUrl = newImageUrl;
        }

        if (form.Id == 0) _db.MenuItems.Add(m);

        // Sizes: replaced as a set. A package's price is its cheapest size ("from").
        _db.ItemOptions.RemoveRange(m.Options);
        m.Options.Clear();
        if (isPackage)
        {
            var order = 1;
            foreach (var o in options)
                m.Options.Add(new ItemOption { Label = o.Label!.Trim(), Price = o.Price, SortOrder = order++ });
            m.Price = options.Min(o => o.Price);
        }
        else
        {
            m.Price = form.Price;
        }

        await _db.SaveChangesAsync();
        TempData["Saved"] = m.Name + " saved.";
        return RedirectToAction(nameof(Index));
    }

    // Turn a whole section on or off: "daily" foods or "packages"
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleOffer(string kind)
    {
        var store = await _stores.StoreAsync(track: true);
        if (kind == "daily")
        {
            if (store.OffersDailyMenu && !store.OffersPackages)
            {
                TempData["Warn"] = "Keep at least one on. To pause all orders, use the Open / Closed switch on the Orders page.";
                return RedirectToAction(nameof(Index));
            }
            store.OffersDailyMenu = !store.OffersDailyMenu;
            TempData["Saved"] = store.OffersDailyMenu ? "Daily foods are on. Customers can order them." : "Daily foods are off. Customers can't see or order them.";
        }
        else if (kind == "packages")
        {
            if (store.OffersPackages && !store.OffersDailyMenu)
            {
                TempData["Warn"] = "Keep at least one on. To pause all orders, use the Open / Closed switch on the Orders page.";
                return RedirectToAction(nameof(Index));
            }
            store.OffersPackages = !store.OffersPackages;
            TempData["Saved"] = store.OffersPackages ? "Food packages are on. Customers can order them." : "Food packages are off. Customers can't see or order them.";
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Food packages only: one-tap "offered" switch
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var sid = await _stores.RequireAsync();
        var m = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == sid);
        if (m != null)
        {
            m.IsAvailable = !m.IsAvailable;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Copy the dishes of the most recent earlier menu into today's menu (only dishes not already there)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyYesterday()
    {
        var sid = await _stores.RequireAsync();
        var today = DateTime.Today;
        var lastDay = await _db.DailyMenuEntries.Where(e => e.Date < today && e.MenuItem!.StoreId == sid).MaxAsync(e => (DateTime?)e.Date);
        if (lastDay == null)
        {
            TempData["Warn"] = "There is no earlier menu to copy yet. Pick today's dishes below.";
            return RedirectToAction(nameof(Index));
        }

        var have = await _db.DailyMenuEntries.Where(e => e.Date == today && e.MenuItem!.StoreId == sid).Select(e => e.MenuItemId).ToListAsync();
        var earlier = await _db.DailyMenuEntries.Include(e => e.MenuItem)
            .Where(e => e.Date == lastDay && !have.Contains(e.MenuItemId) && e.MenuItem != null && e.MenuItem.StoreId == sid && e.MenuItem.Category != MenuItem.PackageCategory)
            .ToListAsync();

        // Portions are not copied (yesterday's number is what was LEFT). The owner sets them if they limit portions.
        foreach (var e in earlier)
            _db.DailyMenuEntries.Add(new DailyMenuEntry { MenuItemId = e.MenuItemId, Date = today });
        await _db.SaveChangesAsync();

        TempData["Saved"] = earlier.Count == 0
            ? "Today's menu already has all of " + lastDay.Value.ToString("MMM d") + "'s dishes."
            : "Copied " + earlier.Count + " dish" + (earlier.Count == 1 ? "" : "es") + " from " + lastDay.Value.ToString("MMM d") + ". Set today's portions if you limit them.";
        return RedirectToAction(nameof(Index));
    }

    // Day one: sample dishes so a new owner can see how it works and then change them
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> StarterMenu()
    {
        var sid = await _stores.RequireAsync();
        var added = DemoData.AddStarterMenu(_db, sid);
        TempData[added == 0 ? "Warn" : "Saved"] = added == 0
            ? "You already have dishes, so nothing was added."
            : added + " sample dishes were added and today's menu is ready. Change the names, prices and photos any time.";
        return RedirectToAction(nameof(Index));
    }

    // Add a catalog dish to today's menu
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToday(int menuItemId, int? stock)
    {
        var sid = await _stores.RequireAsync();
        var today = DateTime.Today;
        var already = await _db.DailyMenuEntries.AnyAsync(e => e.MenuItemId == menuItemId && e.Date == today);
        if (!already)
        {
            var m = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == menuItemId && x.StoreId == sid);
            if (m != null && !m.IsPackage)
            {
                // How many servings are available today (empty = no limit)
                int? servings = stock is > 0 ? Math.Min(stock.Value, 9999) : null;
                _db.DailyMenuEntries.Add(new DailyMenuEntry { MenuItemId = menuItemId, Date = today, Stock = servings });
                await _db.SaveChangesAsync();
                TempData["Saved"] = m.Name + " added to today's menu" + (servings != null ? " with " + servings + " servings." : " with no limit on servings.");
            }
        }
        return RedirectToAction(nameof(Index));
    }

    // Take a dish off today's menu. Past orders keep their own snapshot, so this is safe.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveToday(int entryId)
    {
        var sid = await _stores.RequireAsync();
        var entry = await _db.DailyMenuEntries.Include(e => e.MenuItem).FirstOrDefaultAsync(e => e.Id == entryId && e.MenuItem!.StoreId == sid);
        if (entry != null)
        {
            _db.DailyMenuEntries.Remove(entry);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Portions left today for one of today's dishes
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStock(int entryId, int? stock)
    {
        var sid = await _stores.RequireAsync();
        var entry = await _db.DailyMenuEntries.Include(e => e.MenuItem).FirstOrDefaultAsync(e => e.Id == entryId && e.MenuItem!.StoreId == sid);
        if (entry != null)
        {
            entry.Stock = stock is < 0 ? 0 : stock;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Preparing <-> Ready to serve. Marking a dish ready also updates any open
    // orders that are still waiting on this dish.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleReady(int entryId)
    {
        var sid = await _stores.RequireAsync();
        var entry = await _db.DailyMenuEntries.Include(e => e.MenuItem).FirstOrDefaultAsync(e => e.Id == entryId && e.MenuItem!.StoreId == sid);
        if (entry != null)
        {
            entry.ReadyState = entry.ReadyState == DishReadyState.Preparing
                ? DishReadyState.ReadyToServe
                : DishReadyState.Preparing;

            if (entry.ReadyState == DishReadyState.ReadyToServe)
            {
                var waiting = await _db.OrderItems
                    .Include(oi => oi.Order)
                    .Where(oi => oi.DailyMenuEntryId == entryId && !oi.IsReadyToServe
                        && oi.Order!.Status != OrderStatus.Completed && oi.Order.Status != OrderStatus.Cancelled)
                    .ToListAsync();
                foreach (var oi in waiting) oi.IsReadyToServe = true;
            }

            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var sid = await _stores.RequireAsync();
        var m = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == sid);
        if (m != null)
        {
            _db.MenuItems.Remove(m);   // its sizes and today's-menu entry are removed with it
            await _db.SaveChangesAsync();
            TempData["Saved"] = m.Name + " deleted.";
        }
        return RedirectToAction(nameof(Index));
    }
}
