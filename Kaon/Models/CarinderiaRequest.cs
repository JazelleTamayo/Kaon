using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// A carinderia owner asking to join Kaon (sent from the public "For carinderias" page).
// The Kaon admin reviews it and creates the owner account. Nothing here is a login.
public class CarinderiaRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter your carinderia's name."), StringLength(100)]
    public string CarinderiaName { get; set; } = "";

    [Required(ErrorMessage = "Please enter the owner's name."), StringLength(100)]
    public string OwnerName { get; set; } = "";

    [Required(ErrorMessage = "Please enter a mobile number."), StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please enter your email."),
     EmailAddress(ErrorMessage = "That email doesn't look right."), StringLength(150)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please enter the carinderia's address."), StringLength(300)]
    public string Address { get; set; } = "";

    [StringLength(500)]
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // New -> Approved (account created) or Declined
    [StringLength(20)]
    public string Status { get; set; } = "New";
}

public class ApplyForm : CarinderiaRequest
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to the Privacy Notice.")]
    public bool AcceptPrivacy { get; set; }

    // Hidden field only bots fill in
    public string? Website { get; set; }
}
