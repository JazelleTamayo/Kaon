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

    // "Owner" or "Customer"
    [Required, StringLength(20)]
    public string Role { get; set; } = "Customer";
}