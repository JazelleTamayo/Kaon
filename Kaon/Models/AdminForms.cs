using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

public class ChangePasswordForm
{
    [Required(ErrorMessage = "Please enter your current password.")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Please enter a new password."),
     MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your new password."),
     Compare(nameof(NewPassword), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class ForgotPasswordForm
{
    [Required(ErrorMessage = "Please enter your email."),
     EmailAddress(ErrorMessage = "That email doesn't look right."), StringLength(150)]
    public string Email { get; set; } = "";
}

public class SetPasswordForm
{
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Please choose a password."),
     MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password."),
     Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class CreateOwnerForm
{
    [Required(ErrorMessage = "Please enter the owner's name."), StringLength(100)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Please enter the owner's email."),
     EmailAddress(ErrorMessage = "That email doesn't look right."), StringLength(150)]
    public string Email { get; set; } = "";

    [StringLength(20)]
    public string? Phone { get; set; }

    // Leave empty to open a NEW carinderia for this owner, or pick a carinderia that already exists
    public int? StoreId { get; set; }

    [StringLength(100)]
    public string? CarinderiaName { get; set; }
}

public class AdminDashVm
{
    public List<AppUser> Owners { get; set; } = new();

    // Business numbers for the Kaon team (no customers, no orders)
    public int PayingCount { get; set; }
    public int TrialCount { get; set; }
    public int EndedCount { get; set; }
    // Every carinderia on Kaon (the admin sees the stores, never their customers or orders)
    public List<StoreSettings> Stores { get; set; } = new();

    // Counts per carinderia (store id -> numbers)
    public Dictionary<int, StoreStats> Stats { get; set; } = new();

    // Owners who have an invitation link that is still waiting to be used, and when it expires
    public Dictionary<int, DateTime> PendingLinks { get; set; } = new();
}
