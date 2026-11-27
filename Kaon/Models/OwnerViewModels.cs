using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

public class BoardVm
{
    public List<Order> Active { get; set; } = new();
    public int OrdersToday { get; set; }
    public decimal SalesToday { get; set; }
    public StoreSettings Store { get; set; } = new();

    // Module 7: saved riders to choose from on delivery orders
    public List<Rider> Riders { get; set; } = new();

    // Food-package pre-orders get their own lane on the board, separate from same-day meals
    public List<Order> PackagePending { get; set; } = new();
    public List<Order> PackageReserved { get; set; } = new();
    public List<Order> PackageKitchen { get; set; } = new();
}

public class SummaryVm
{
    public DateTime Date { get; set; }
    public List<Order> Orders { get; set; } = new();
    public StoreSettings Store { get; set; } = new();

    public List<Order> Completed => Orders.Where(o => o.Status == OrderStatus.Completed).ToList();
    public int CancelledCount => Orders.Count(o => o.Status == OrderStatus.Cancelled);
    public int StillOpenCount => Orders.Count(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled);
    public decimal Sales => Completed.Sum(o => o.Total);
    public decimal DeliveryFees => Completed.Sum(o => o.DeliveryFee);
    public decimal PaidTotal => Completed.Where(o => o.PaymentStatus == PaymentStatus.Paid).Sum(o => o.Total);
    public decimal ToCollect => Completed.Where(o => o.PaymentStatus != PaymentStatus.Paid).Sum(o => o.Total);
    public int PickupCount => Completed.Count(o => o.OrderType == OrderType.Pickup);
    public int DeliveryCount => Completed.Count(o => o.OrderType == OrderType.Delivery);

    public IEnumerable<(string Name, int Qty, decimal Amount)> TopItems => Completed
        .SelectMany(o => o.Items)
        .GroupBy(i => i.ItemName)
        .Select(g => (g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.Quantity * i.UnitPrice)))
        .OrderByDescending(x => x.Item2)
        .ThenBy(x => x.Key);
}

public class OwnerMenuVm
{
    public List<MenuItem> CatalogMeals { get; set; } = new();
    public List<DailyMenuEntry> TodayEntries { get; set; } = new();
    public List<MenuItem> Packages { get; set; } = new();
    public StoreSettings Store { get; set; } = new();

    // The most recent earlier day that had a menu (so "Copy yesterday's menu" can be offered)
    public DateTime? LastMenuDate { get; set; }
}

public class OptionForm
{
    [StringLength(80)]
    public string? Label { get; set; }

    [Range(0, 1000000, ErrorMessage = "Enter a price.")]
    public decimal Price { get; set; }
}

public class MenuForm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter the item name."), StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(300)]
    public string? Description { get; set; }

    [Range(0, 1000000, ErrorMessage = "Enter a price.")]
    public decimal Price { get; set; }

    [Required]
    public string Category { get; set; } = "Meal";

    public string? Series { get; set; }

    [Range(0, 1000, ErrorMessage = "Enter 0 to 1,000, or leave empty for no limit.")]
    public int? Stock { get; set; }

    public bool IsAvailable { get; set; } = true;

    // Food packages: sizes or portions with their prices
    public List<OptionForm> Options { get; set; } = new();

    // New photo to upload. Leave empty to keep the current one.
    public IFormFile? Image { get; set; }
    public string? ExistingImageUrl { get; set; }
}
