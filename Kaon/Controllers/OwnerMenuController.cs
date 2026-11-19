using Kaon.Data;
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
    public OwnerMenuController(KaonDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.Store = await _db.StoreSettings.AsNoTracking().FirstAsync();
        var items = await _db.MenuItems
            .Include(m => m.Options)
            .OrderBy(m => m.Name)
            .ToListAsync();
        return View(items);
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
        var m = await _db.MenuItems.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        FillForm();
        return View(new MenuForm
        {
            Id = m.Id, Name = m.Name, Description = m.Description, Price = m.Price,
            Category = m.Category, Series = m.SeriesName, Stock = m.Stock, IsAvailable = m.IsAvailable,
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

        MenuItem? m = form.Id == 0
            ? new MenuItem()
            : await _db.MenuItems.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == form.Id);
        if (m == null) return NotFound();

        m.Name = form.Name.Trim();
        m.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        m.Category = form.Category;
        m.Series = string.IsNullOrEmpty(form.Series) ? MenuSeries.Other : form.Series;
        m.Stock = isPackage ? null : form.Stock;
        m.IsAvailable = form.IsAvailable;

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
        var store = await _db.StoreSettings.FirstAsync();
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

    // One-tap "cooking today" switch
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var m = await _db.MenuItems.FindAsync(id);
        if (m != null)
        {
            m.IsAvailable = !m.IsAvailable;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await _db.MenuItems.FindAsync(id);
        if (m != null)
        {
            _db.MenuItems.Remove(m);   // its sizes are removed with it
            await _db.SaveChangesAsync();
            TempData["Saved"] = m.Name + " deleted.";
        }
        return RedirectToAction(nameof(Index));
    }
}
