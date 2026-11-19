using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// Whether a daily-menu dish's current batch is still on the stove or already done.
// Food packages don't use this: they're made to order for a scheduled date.
// ReadyToServe is the zero value: a fresh column (or a brand-new item) defaults
// to it, so adding this feature never retroactively marks existing dishes as Preparing.
public enum DishReadyState
{
    ReadyToServe,
    Preparing
}

public class MenuItem
{
    public const string PackageCategory = "Food package";

    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(300)]
    public string? Description { get; set; }

    // For a food package with sizes, this is the starting ("from") price
    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    // "Meal" (daily menu) or "Food package" (party trays, lechon)
    [StringLength(30)]
    public string Category { get; set; } = "Meal";

    // Chicken, Pork, Beef, ... used for browsing and filtering
    [StringLength(30)]
    public string? Series { get; set; }

    // Photo shown to customers, for example /img/menu/adobo.webp
    [StringLength(200)]
    public string? ImageUrl { get; set; }

    // Manual on/off switch ("cooking today")
    public bool IsAvailable { get; set; } = true;

    // Preparing vs ready to serve right now (daily menu items only)
    public DishReadyState ReadyState { get; set; } = DishReadyState.ReadyToServe;

    // Portions left today. Null means no limit.
    public int? Stock { get; set; }

    // Sizes or portions (food packages only)
    public List<ItemOption> Options { get; set; } = new();

    [NotMapped]
    public bool IsPackage => Category == PackageCategory;

    [NotMapped]
    public string SeriesName => MenuSeries.Of(Series);

    // Customers can order only when switched on and not out of stock
    [NotMapped]
    public bool CanOrder => IsAvailable && (Stock == null || Stock > 0);

    // The price to show: "from" the cheapest size when there are sizes
    [NotMapped]
    public decimal FromPrice => Options.Any() ? Options.Min(o => o.Price) : Price;
}
