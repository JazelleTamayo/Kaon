using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, StringLength(150)]
    public string Email { get; set; } = "";

    [StringLength(20)]
    public string? Phone { get; set; }

    // Customers: the delivery address saved at checkout, filled in for the next order
    [StringLength(300)]
    public string? Address { get; set; }

    [Required]
    public string PasswordHash { get; set; } = "";

    // "Admin" (Kaon team), "Owner" (carinderia) or "Customer"
    [Required, StringLength(20)]
    public string Role { get; set; } = "Customer";

    // The Kaon team can turn an account off
    public bool IsActive { get; set; } = true;

    // The one carinderia this account belongs to. An owner runs it; a customer signed up at it
    // (each carinderia has its own customers). Kaon admins belong to none.
    public int? StoreId { get; set; }

    // Protection against password guessing: 5 wrong passwords lock the account for 10 minutes
    public int FailedLogins { get; set; }
    public DateTime? LockoutUntil { get; set; }

    // True while the person is using a temporary password
    public bool MustChangePassword { get; set; }
}