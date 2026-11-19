using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int MenuItemId { get; set; }

    // Saved copy, so old orders keep their name and price if the menu changes
    public string ItemName { get; set; } = "";

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; } = 1;

    // Snapshot of the dish's readiness at order time (daily menu items only, not food packages)
    public bool TracksReadiness { get; set; }
    public bool IsReadyToServe { get; set; }

    // Which day's menu entry this came from (daily menu items only). Loose reference,
    // like MenuItemId: no navigation property, so removing the entry never blocks here.
    public int? DailyMenuEntryId { get; set; }
}