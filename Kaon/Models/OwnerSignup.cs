using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// A carinderia owner creating their own account and carinderia
public class OwnerSignupForm
{
    [Required(ErrorMessage = "Please enter your carinderia's name."), StringLength(100)]
    public string CarinderiaName { get; set; } = "";

    [Required(ErrorMessage = "Please enter your full name."), StringLength(100)]
    public string OwnerName { get; set; } = "";

    [Required(ErrorMessage = "Please enter a mobile number."), StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please enter your email."),
     EmailAddress(ErrorMessage = "That email doesn't look right."), StringLength(150)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please choose a password."),
     MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password."),
     Compare(nameof(Password), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to the Privacy Notice.")]
    public bool AcceptPrivacy { get; set; }

    // Hidden field only bots fill in
    public string? Website { get; set; }
}
