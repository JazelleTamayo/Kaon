using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// What the Kaon team may see about one carinderia: its menu size and its plan.
// Never customers or orders: how many customers or orders a carinderia has is that carinderia's own business.
public class StoreStats
{
    public int Dishes { get; set; }

    // "Free trial", "Paying", "Ended" or "No owner"
    public string Plan { get; set; } = "No owner";
    public string PlanClass { get; set; } = "chip-soldout";
    public DateTime? AccessUntil { get; set; }
    public int DaysLeft { get; set; }
}

public class StoreDetailVm
{
    public StoreSettings Store { get; set; } = new();
    public string Link { get; set; } = "";
    public List<AppUser> Owners { get; set; } = new();
    public Dictionary<int, DateTime> PendingLinks { get; set; } = new();
    public StoreStats Stats { get; set; } = new();

    public List<ActivityLog> Activity { get; set; } = new();

    // Module 12: printed QR code scans and the sign-ups that followed
    public QrStats Qr { get; set; } = new();

    // Module 14: feedback numbers only (no customer names or comments)
    public FeedbackStats Feedback { get; set; } = new();
}

// What the carinderia's customer page shows, for the owner or admin to look at (nothing can be ordered)
public class StorePreviewVm
{
    public StoreSettings Store { get; set; } = new();
    public List<DailyMenuEntry> Entries { get; set; } = new();
    public List<MenuItem> Packages { get; set; } = new();
}

public class RenameStoreForm
{
    [Required(ErrorMessage = "Please enter the carinderia's name."), StringLength(100)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Please enter the web address."), StringLength(60)]
    public string Slug { get; set; } = "";
}

public class AddDishForm
{
    [Required(ErrorMessage = "Please enter the dish name."), StringLength(100)]
    public string Name { get; set; } = "";

    [Range(1, 10000, ErrorMessage = "Enter a price above 0.")]
    public decimal Price { get; set; }

    [StringLength(30)]
    public string? Series { get; set; }
}
