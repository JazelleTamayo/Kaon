using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// Module 14: Feedback & Surveys. Collects the validation data the workbook says must be measured:
//  - customers: is the fixed delivery fee reasonable (target 70% yes), how much waiting time was saved, a rating
//  - owners: a log of mixed-up, forgotten or late orders (before/after Kaon), and how much the
//    pickup/delivery separation helps

// One per order, given by the customer after the order is completed
public class OrderFeedback
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public int StoreId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    // Delivery orders only
    public bool? FeeReasonable { get; set; }

    // A choice from TimeSaved.Options (null = not answered)
    public int? TimeSaved { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class TimeSaved
{
    // (code, words, minutes used for the average)
    public static readonly (int Code, string Text, int Minutes)[] Options =
    {
        (0, "No time saved", 0),
        (1, "Less than 10 minutes", 5),
        (2, "10 to 20 minutes", 15),
        (3, "20 to 30 minutes", 25),
        (4, "More than 30 minutes", 35)
    };

    public static string Text(int? code) => Options.FirstOrDefault(o => o.Code == code).Text ?? "";
}

public enum ProblemKind
{
    MixedUp,     // wrong items or given to the wrong person
    Forgotten,   // missed or never made
    Delayed,     // much later than promised
    Other
}

// The owner writes down an order that went wrong, to compare with how often it happened before Kaon
public class OrderProblem
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    public ProblemKind Kind { get; set; }

    // The Kaon order it was, if any (problems with walk-in orders can be logged too)
    public int? OrderId { get; set; }

    [StringLength(300)]
    public string? Note { get; set; }

    public DateTime At { get; set; } = DateTime.Now;
}

// The owner's own answers. A new row each time, so the answers can be followed over time.
public class OwnerSurvey
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public int UserId { get; set; }

    // "How much does keeping pickup and delivery orders apart help you?" 1 (not at all) to 5 (a lot)
    [Range(1, 5, ErrorMessage = "Choose from 1 to 5.")]
    public int SeparationRating { get; set; }

    // "Before Kaon, about how many orders a week were mixed up, forgotten or late?"
    [Range(0, 500, ErrorMessage = "Enter a number from 0 to 500.")]
    public int? ProblemsPerWeekBefore { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class ProblemText
{
    public static string Label(ProblemKind k) => k switch
    {
        ProblemKind.MixedUp => "Mixed up",
        ProblemKind.Forgotten => "Forgotten",
        ProblemKind.Delayed => "Late",
        _ => "Other"
    };

    public static string Hint(ProblemKind k) => k switch
    {
        ProblemKind.MixedUp => "Wrong food, or given to the wrong person",
        ProblemKind.Forgotten => "Missed, or never made",
        ProblemKind.Delayed => "Much later than promised",
        _ => "Something else went wrong"
    };
}

public class FeedbackForm
{
    [Range(1, 5, ErrorMessage = "Please choose from 1 to 5 stars.")]
    public int Rating { get; set; }

    public bool? FeeReasonable { get; set; }

    [Range(0, 4)]
    public int? TimeSaved { get; set; }

    [StringLength(500, ErrorMessage = "Please keep it under 500 characters.")]
    public string? Comment { get; set; }
}

// Numbers for one carinderia, shown to its owner and (numbers only) to the Kaon admin
public class FeedbackStats
{
    public int Responses { get; set; }
    public double? AverageRating { get; set; }
    public int FeeAnswers { get; set; }
    public int FeeYes { get; set; }
    public double? FeeYesPercent => FeeAnswers == 0 ? null : 100.0 * FeeYes / FeeAnswers;
    public int TimeAnswers { get; set; }
    public double? AverageMinutesSaved { get; set; }
    public List<(string Text, int Count)> TimeSavedCounts { get; set; } = new();
    public int[] Stars { get; set; } = new int[5];   // how many 1..5 star ratings

    // Owner's problem log
    public int ProblemsLast7Days { get; set; }
    public double ProblemsPerWeekLast4Weeks { get; set; }
    public int? ProblemsPerWeekBefore { get; set; }
    public List<(string Kind, int Count)> ProblemKindsLast30Days { get; set; } = new();

    // Owner survey (latest answer)
    public int? SeparationRating { get; set; }
    public DateTime? OwnerSurveyAt { get; set; }

    public const double FeeTargetPercent = 70;
}

public class OwnerFeedbackVm
{
    public FeedbackStats Stats { get; set; } = new();
    public List<OrderFeedback> Recent { get; set; } = new();
    public List<OrderProblem> Problems { get; set; } = new();
    public OwnerSurvey? LastSurvey { get; set; }
    public decimal DeliveryFee { get; set; }
}
