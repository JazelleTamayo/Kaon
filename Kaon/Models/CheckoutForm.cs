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

    // Keep this address on the customer's profile for next time
    public bool SaveAddress { get; set; } = true;

    // How the customer will pay
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [StringLength(60)]
    public string? PaymentRef { get; set; }

    // Screenshot of the GCash / bank payment (cashless only)
    public IFormFile? Proof { get; set; }

    // Needed only when the order has a food package
    public DateTime? ScheduledFor { get; set; }

    // Ordinary meals: preferred pickup or delivery time today. Empty means as soon as possible.
    public TimeSpan? PreferredTime { get; set; }

    [StringLength(300, ErrorMessage = "Please keep your note under 300 characters.")]
    public string? Notes { get; set; }
}