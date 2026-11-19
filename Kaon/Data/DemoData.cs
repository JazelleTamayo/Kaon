using Kaon.Models;

namespace Kaon.Data;

// Starter data so the app is never empty on first run.
// All prices here are SAMPLE prices. The owner changes them on the Menu page.
public static class DemoData
{
    // The very first starter menu (no photos). If the menu is still exactly this,
    // it is replaced with the full starter menu below. An owner-edited menu is never touched.
    private static readonly string[] OriginalNames =
    {
        "Chicken Adobo", "Pork Sinigang", "Pancit Canton", "Kare-Kare",
        "Family Food Package A", "Family Food Package B"
    };

    // Series for the starter meals, used when upgrading an existing database
    private static readonly Dictionary<string, string> MealSeries = new()
    {
        ["Chicken Adobo"] = "Chicken", ["Fried Chicken"] = "Chicken", ["Chicken Afritada"] = "Chicken",
        ["Beef Kaldereta"] = "Beef", ["Pinakbet"] = "Vegetables", ["Pancit Bihon"] = "Noodles & Pasta",
        ["Crispy Bola-Bola"] = "Pork"
    };

    public static void Seed(KaonDbContext db, string? demoGCash = null)
    {
        var store = db.StoreSettings.OrderBy(s => s.Id).First();
        if (store.Name == "Kaon Carinderia")
            store.Name = "Jamaica Foods";
        // The demo GCash number comes from a private setting (never from the code)
        if (!string.IsNullOrWhiteSpace(demoGCash) && (string.IsNullOrWhiteSpace(store.GCashNumber) || store.GCashNumber == "09XX XXX XXXX"))
            store.GCashNumber = demoGCash.Trim();

        // The first carinderia gets a readable web address (/c/jamaica-foods) instead of the placeholder
        if (string.IsNullOrEmpty(store.Slug) || store.Slug == "carinderia-" + store.Id)
        {
            var wanted = Kaon.Helpers.StoreContext.Slugify(store.Name);
            var slug = wanted;
            for (var n = 2; db.StoreSettings.Any(x => x.Slug == slug && x.Id != store.Id); n++) slug = wanted + "-" + n;
            store.Slug = slug;
        }

        // Everything below is the starter data of this first carinderia only
        var sid = store.Id;
        var items = db.MenuItems.Where(m => m.StoreId == sid).ToList();
        var untouched = items.Count == 0 ||
            (items.Count == OriginalNames.Length &&
             items.All(m => m.ImageUrl == null) &&
             items.Select(m => m.Name).OrderBy(n => n).SequenceEqual(OriginalNames.OrderBy(n => n)));

        if (untouched)
        {
            db.MenuItems.RemoveRange(items);
            var meals = StarterMeals();
            var packages = StarterPackages();
            foreach (var m in meals.Concat(packages)) m.StoreId = sid;
            db.MenuItems.AddRange(meals);
            db.MenuItems.AddRange(packages);
            store.StarterPackagesAdded = true;
            db.SaveChanges();   // assign ids before seeding today's menu below

            SeedTodayMenu(db, meals);
        }
        else if (!store.StarterPackagesAdded)
        {
            // Upgrade an existing database once: add series, retire the old fixed
            // "Family Food Package A/B", and add the party trays with sizes.
            foreach (var m in items.Where(m => m.Category != MenuItem.PackageCategory && string.IsNullOrEmpty(m.Series)))
                if (MealSeries.TryGetValue(m.Name, out var s)) m.Series = s;

            var oldFixed = items.Where(m => m.Category == MenuItem.PackageCategory &&
                                            m.Name.StartsWith("Family Food Package")).ToList();
            db.MenuItems.RemoveRange(oldFixed);

            var have = items.Select(m => m.Name).ToHashSet();
            var addPk = StarterPackages().Where(p => !have.Contains(p.Name)).ToList();
            foreach (var m in addPk) m.StoreId = sid;
            db.MenuItems.AddRange(addPk);
            store.StarterPackagesAdded = true;
        }

        db.SaveChanges();

        // Upgrading from before the daily menu existed: customers would suddenly see an empty
        // menu. Carry the meals that were available over to today, ONCE (never again, so an owner
        // who clears today's menu on purpose does not get it back at the next restart).
        if (!store.DailyMenuSeeded)
        {
            if (!db.DailyMenuEntries.Any(e => e.MenuItem!.StoreId == sid))
            {
                var today = DateTime.Today;
                foreach (var m in db.MenuItems.Where(m => m.StoreId == sid && m.Category != MenuItem.PackageCategory && m.IsAvailable).ToList())
                    db.DailyMenuEntries.Add(new DailyMenuEntry { MenuItemId = m.Id, Date = today, Stock = m.Stock });
            }
            store.DailyMenuSeeded = true;
            db.SaveChanges();
        }
    }

    // Day-one "today's menu" so a fresh demo isn't empty. Crispy Bola-Bola is left off today's
    // menu entirely (still in the catalog); Beef Kaldereta starts with limited portions, still Preparing.
    private static readonly Dictionary<string, int?> DayOneStock = new() { ["Beef Kaldereta"] = 5 };
    private static readonly Dictionary<string, DishReadyState> DayOneReady = new() { ["Beef Kaldereta"] = DishReadyState.Preparing };
    private static readonly HashSet<string> SkipToday = new() { "Crispy Bola-Bola" };

    private static void SeedTodayMenu(KaonDbContext db, List<MenuItem> meals)
    {
        var today = DateTime.Today;
        foreach (var m in meals.Where(m => !SkipToday.Contains(m.Name)))
        {
            db.DailyMenuEntries.Add(new DailyMenuEntry
            {
                MenuItemId = m.Id,
                Date = today,
                Stock = DayOneStock.GetValueOrDefault(m.Name),
                ReadyState = DayOneReady.GetValueOrDefault(m.Name, DishReadyState.ReadyToServe)
            });
        }
    }

    private static List<MenuItem> StarterMeals() => new()
    {
        new() { Name = "Chicken Adobo", Description = "Braised in soy sauce and vinegar, with rice", Price = 70m, Category = "Meal", Series = "Chicken", ImageUrl = "/img/menu/adobo.webp" },
        new() { Name = "Fried Chicken", Description = "Crispy and juicy, with rice", Price = 85m, Category = "Meal", Series = "Chicken", ImageUrl = "/img/menu/fried-chicken.webp" },
        new() { Name = "Chicken Afritada", Description = "Tomato stew with potatoes and carrots, with rice", Price = 80m, Category = "Meal", Series = "Chicken", ImageUrl = "/img/menu/afritada.webp" },
        new() { Name = "Beef Kaldereta", Description = "Slow-cooked beef stew with olives, with rice", Price = 95m, Category = "Meal", Series = "Beef", ImageUrl = "/img/menu/kaldereta.webp" },
        new() { Name = "Pinakbet", Description = "Mixed vegetables with pork, with rice", Price = 65m, Category = "Meal", Series = "Vegetables", ImageUrl = "/img/menu/pinakbet.webp" },
        new() { Name = "Pancit Bihon", Description = "Stir-fried rice noodles with vegetables", Price = 60m, Category = "Meal", Series = "Noodles & Pasta", ImageUrl = "/img/menu/pancit-bihon.webp" },
        new() { Name = "Crispy Bola-Bola", Description = "Fried bites with sweet chili sauce", Price = 50m, Category = "Meal", Series = "Pork", ImageUrl = "/img/menu/bola-bola.webp" }
    };

    // Party trays and lechon. Customers pick a size, then say when they need it.
    private static List<MenuItem> StarterPackages() => new()
    {
        Tray("Pancit Bihon-Canton", "Noodles & Pasta", "Guisado with chicken, pork and vegetables", "/img/menu/pancit-tray.webp", 450, 750, 1200),
        Tray("Chicken Roulade", "Chicken", "Stuffed chicken rolls in creamy sauce", "/img/menu/chicken-roulade.webp", 650, 1100, 1700),
        Tray("Pork Humba", "Pork", "Sweet-savory braised pork belly", "/img/menu/pork-humba.webp", 700, 1200, 1900),
        Tray("Baked Lasagna", "Noodles & Pasta", "Baked pasta with meat sauce and cheese", "/img/menu/lasagna.webp", 600, 1000, 1600),
        Tray("Crispy Chicken Fillet", "Chicken", "Breaded and fried, with lemon wedges", "/img/menu/chicken-fillet.webp", 550, 950, 1500),
        new()
        {
            Name = "Lechon Baboy", Series = "Pork", Category = MenuItem.PackageCategory,
            Description = "Crispy skin, juicy meat. Order by the kilo, by half, or the whole lechon.",
            ImageUrl = "/img/menu/lechon.webp", Price = 950m,
            Options =
            {
                new ItemOption { Label = "Per kilo (cut portion)", Price = 950m, SortOrder = 1 },
                new ItemOption { Label = "Half lechon (about 5 kg)", Price = 4800m, SortOrder = 2 },
                new ItemOption { Label = "Whole lechon, 8 kg", Price = 7500m, SortOrder = 3 },
                new ItemOption { Label = "Whole lechon, 10 kg", Price = 9200m, SortOrder = 4 },
                new ItemOption { Label = "Whole lechon, 12 kg", Price = 11000m, SortOrder = 5 }
            }
        }
    };

    private static MenuItem Tray(string name, string series, string description, string image, decimal small, decimal medium, decimal large) => new()
    {
        Name = name, Series = series, Description = description, ImageUrl = image,
        Category = MenuItem.PackageCategory, Price = small,
        Options =
        {
            new ItemOption { Label = "Small tray (6-8 people)", Price = small, SortOrder = 1 },
            new ItemOption { Label = "Medium tray (10-12 people)", Price = medium, SortOrder = 2 },
            new ItemOption { Label = "Large tray (18-20 people)", Price = large, SortOrder = 3 }
        }
    };
}
