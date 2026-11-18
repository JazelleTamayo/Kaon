using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

public class CheckoutForm
{
    [Required(ErrorMessage = "Please enter your name."), StringLength(100)]
    public string CustomerName { get; set; } = "";

    [Required(ErrorMessage = "Please enter your phone number."), StringLength(20)]
    public string Phone { get; set; } = "";

    public OrderType OrderType { get; set; } = OrderType.Pickup;

    [StringLength(300)]
    public string? Address { get; set; }

    // Needed only when the order has a food package
    public DateTime? ScheduledFor { get; set; }
}