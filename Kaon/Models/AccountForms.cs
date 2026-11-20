using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

public class LoginForm
{
    [Required(ErrorMessage = "Please enter your email.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please enter your password.")]
    public string Password { get; set; } = "";
}

public class RegisterForm
{
    [Required(ErrorMessage = "Please enter your name."), StringLength(100)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Please enter your phone number."), StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please enter your email."),
     EmailAddress(ErrorMessage = "That email doesn't look right."), StringLength(150)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please enter a password."),
     MinLength(6, ErrorMessage = "Use at least 6 characters.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password."),
     Compare(nameof(Password), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    // Data Privacy: the customer must agree to the Privacy Notice
    [Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to the Privacy Notice to continue.")]
    public bool AcceptPrivacy { get; set; }
}
// "My account": the details a person may change about themselves (email and role stay as they are)
public class ProfileForm
{
    [Required(ErrorMessage = "Please enter your name."), StringLength(100)]
    public string FullName { get; set; } = "";

    [StringLength(20)]
    public string? Phone { get; set; }
}
