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

    [Required]
    public string PasswordHash { get; set; } = "";

    // "Admin" (Kaon team), "Owner" (carinderia) or "Customer"
    [Required, StringLength(20)]
    public string Role { get; set; } = "Customer";

    // The Kaon team can turn an account off
    public bool IsActive { get; set; } = true;

    // Protection against password guessing: 5 wrong passwords lock the account for 10 minutes
    public int FailedLogins { get; set; }
    public DateTime? LockoutUntil { get; set; }

    // True while the person is using a temporary password
    public bool MustChangePassword { get; set; }
}