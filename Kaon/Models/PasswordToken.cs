using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// A one-time link that lets a person choose their own password.
// Only a fingerprint (hash) of the link is stored, never the link itself.
public class PasswordToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, StringLength(64)]
    public string TokenHash { get; set; } = "";

    // "Invite" (new account) or "Reset" (forgotten password)
    [Required, StringLength(10)]
    public string Purpose { get; set; } = "Invite";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
