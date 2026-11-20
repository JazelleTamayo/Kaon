using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// Module 9: Subscription & Billing.
// Kaon earns a low, fixed monthly fee from each carinderia (no per-order commission),
// with a free first month. Payment is by GCash: the owner uploads proof, the Kaon admin confirms.

// One row: how Kaon charges every carinderia
public class PlatformSettings
{
    public int Id { get; set; }

    // PLACEHOLDER until the team confirms the real price from pilot feedback
    [Column(TypeName = "decimal(10,2)"), Range(0, 10000, ErrorMessage = "Enter a fee from 0 to 10,000.")]
    public decimal MonthlyFee { get; set; } = 299;

    [Range(0, 365, ErrorMessage = "Enter 0 to 365 days.")]
    public int TrialDays { get; set; } = 30;

    // Where owners send the subscription payment
    [StringLength(80)]
    public string? GCashName { get; set; } = "Kaon";

    [StringLength(30)]
    public string? GCashNumber { get; set; }

    [StringLength(200)]
    public string? PaymentNote { get; set; }
}

public enum SubscriptionStatus
{
    Trial,
    Active,
    Expired
}

// One per carinderia owner
public class Subscription
{
    public const int ReminderDays = 7;

    public int Id { get; set; }

    public int OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The free first month ends here
    public DateTime TrialEndsAt { get; set; }

    // Paid access runs until this date (null = never paid)
    public DateTime? PaidUntil { get; set; }

    // The last time the owner was emailed a reminder (so one click is not sent twice in a row)
    public DateTime? LastReminderAt { get; set; }

    // Automatic reminders: the most urgent one already sent for the current end date
    // (7, 3 or 1 day before, or -1 once it has ended). It starts over when the end date changes.
    public int ReminderStage { get; set; }
    public DateTime? ReminderForAccessUntil { get; set; }

    public List<SubscriptionPayment> Payments { get; set; } = new();

    // The last moment the owner can still use Kaon
    [NotMapped]
    public DateTime AccessUntil => PaidUntil.HasValue && PaidUntil > TrialEndsAt ? PaidUntil.Value : TrialEndsAt;

    public SubscriptionStatus StatusAt(DateTime nowUtc) =>
        nowUtc >= AccessUntil ? SubscriptionStatus.Expired
        : nowUtc < TrialEndsAt ? SubscriptionStatus.Trial
        : SubscriptionStatus.Active;

    public bool HasAccess(DateTime nowUtc) => nowUtc < AccessUntil;

    // Whole days left, rounded up (0 when expired)
    public int DaysLeft(DateTime nowUtc) =>
        HasAccess(nowUtc) ? (int)Math.Ceiling((AccessUntil - nowUtc).TotalDays) : 0;

    public bool IsDueSoon(DateTime nowUtc) => HasAccess(nowUtc) && DaysLeft(nowUtc) <= ReminderDays;
}

public enum SubscriptionPaymentStatus
{
    Pending,    // proof sent, waiting for the Kaon admin
    Confirmed,
    Rejected
}

public class SubscriptionPayment
{
    public int Id { get; set; }

    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public int Months { get; set; } = 1;

    // The fee at the time of payment, so a later price change does not rewrite history
    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    // GCash reference number the owner typed in
    [StringLength(60)]
    public string? Reference { get; set; }

    // File name of the screenshot (kept outside wwwroot, shown only to that owner and the Kaon admin)
    [StringLength(100)]
    public string? Proof { get; set; }

    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Pending;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    [StringLength(200)]
    public string? ReviewNote { get; set; }

    // True when the Kaon admin recorded a payment received in person (no proof upload)
    public bool RecordedByAdmin { get; set; }

    // The access period this payment bought, once confirmed
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}

// Billing rules in one place, so the owner pages and the admin pages agree
public static class Billing
{
    public const int MaxMonths = 12;

    // Confirming a payment adds whole months after the current end of access,
    // so paying during the free month starts billing after the trial, and paying early never loses days.
    public static void Confirm(Subscription sub, SubscriptionPayment payment, DateTime nowUtc)
    {
        var start = sub.AccessUntil > nowUtc ? sub.AccessUntil : nowUtc;
        payment.PeriodStart = start;
        payment.PeriodEnd = start.AddMonths(payment.Months);
        payment.Status = SubscriptionPaymentStatus.Confirmed;
        payment.ReviewedAt = nowUtc;
        sub.PaidUntil = payment.PeriodEnd;
    }

    public static Subscription NewFor(int ownerId, PlatformSettings platform, DateTime nowUtc) => new()
    {
        OwnerId = ownerId,
        CreatedAt = nowUtc,
        TrialEndsAt = nowUtc.AddDays(platform.TrialDays)
    };
}

public class SubmitPaymentForm
{
    [Range(1, Billing.MaxMonths, ErrorMessage = "Choose how many months you paid for.")]
    public int Months { get; set; } = 1;

    [StringLength(60)]
    public string? Reference { get; set; }
}

public class BillingVm
{
    public Subscription Subscription { get; set; } = new();
    public PlatformSettings Platform { get; set; } = new();
    public List<SubscriptionPayment> Payments { get; set; } = new();
    public bool HasPending => Payments.Any(p => p.Status == SubscriptionPaymentStatus.Pending);
}

public class AdminBillingRow
{
    public AppUser Owner { get; set; } = new();
    public Subscription? Subscription { get; set; }
    public int PendingCount { get; set; }
}

public class AdminBillingVm
{
    public PlatformSettings Platform { get; set; } = new();
    public List<AdminBillingRow> Rows { get; set; } = new();
    public List<SubscriptionPayment> PendingPayments { get; set; } = new();
    public List<SubscriptionPayment> RecentPayments { get; set; } = new();
    public decimal ConfirmedTotal { get; set; }

    // Store id -> carinderia name, so every owner is shown with their carinderia
    public Dictionary<int, string> StoreNames { get; set; } = new();
}
