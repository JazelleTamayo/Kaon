using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

public class Order
{
    public int Id { get; set; }

    // Which carinderia this order was sent to
    public int StoreId { get; set; }

    // Which customer placed the order
    public int? UserId { get; set; }

    [Required, StringLength(100)]
    public string CustomerName { get; set; } = "";

    [StringLength(20)]
    public string? Phone { get; set; }

    public OrderType OrderType { get; set; }

    // Only needed for Delivery
    [StringLength(300)]
    public string? Address { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    [Column(TypeName = "decimal(10,2)")]
    public decimal DeliveryFee { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // How the customer pays, and whether it has been paid
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    // Food packages only: 50% of the package subtotal, due up front. The rest is
    // collected in person at pickup/delivery. Null for ordinary meal orders.
    [Column(TypeName = "decimal(10,2)")]
    public decimal? DepositAmount { get; set; }

    [StringLength(60)]
    public string? PaymentRef { get; set; }

    // File name of the proof-of-payment picture (kept outside wwwroot)
    [StringLength(100)]
    public string? PaymentProof { get; set; }

    // When the customer needs the order (food packages and lechon are made ahead)
    // For ordinary meals this is the customer's preferred pickup or delivery time today (optional)
    public DateTime? ScheduledFor { get; set; }

    // Anything the customer wants the carinderia to know ("less rice", "gate is blue")
    [StringLength(300)]
    public string? Notes { get; set; }

    // Why the carinderia cancelled the order, shown to the customer
    [StringLength(200)]
    public string? CancelReason { get; set; }

    // Module 7: the rider taking a delivery order. A copy of the name and number is kept on the order,
    // so the order still shows who delivered it if the rider is later removed.
    [StringLength(60)]
    public string? RiderName { get; set; }

    [StringLength(20)]
    public string? RiderPhone { get; set; }

    // The secret part of the rider's private link (/r/{token}). Cleared when the rider is removed from the order.
    [StringLength(64)]
    public string? RiderToken { get; set; }

    public DateTime? RiderAssignedAt { get; set; }

    // When the order was handed over (delivered, or picked up by the customer)
    public DateTime? CompletedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    [NotMapped]
    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity) + DeliveryFee;

    // A food-package pre-order always carries a deposit; an ordinary meal order never does.
    [NotMapped]
    public bool IsPackage => DepositAmount != null;
}