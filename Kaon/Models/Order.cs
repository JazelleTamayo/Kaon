using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

public class Order
{
    public int Id { get; set; }

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

    // When the customer needs the order (food packages and lechon are made ahead)
    public DateTime? ScheduledFor { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    [NotMapped]
    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity) + DeliveryFee;
}