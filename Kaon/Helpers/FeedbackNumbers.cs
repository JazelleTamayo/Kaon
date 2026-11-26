using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// Module 14: the feedback numbers for one carinderia (owner's Feedback page and the Kaon admin's support view)
public static class FeedbackNumbers
{
    public static async Task<FeedbackStats> ForStoreAsync(KaonDbContext db, int storeId)
    {
        var fb = await db.OrderFeedbacks.AsNoTracking().Where(f => f.StoreId == storeId)
            .Select(f => new { f.Rating, f.FeeReasonable, f.TimeSaved }).ToListAsync();

        var now = DateTime.Now;
        var problems = await db.OrderProblems.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.At >= now.AddDays(-30))
            .Select(p => new { p.Kind, p.At }).ToListAsync();

        var survey = await db.OwnerSurveys.AsNoTracking().Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        // The "before Kaon" number is the most recent answer that gave one
        var before = await db.OwnerSurveys.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.ProblemsPerWeekBefore != null)
            .OrderByDescending(s => s.CreatedAt).Select(s => s.ProblemsPerWeekBefore).FirstOrDefaultAsync();

        var fee = fb.Where(f => f.FeeReasonable != null).ToList();
        var time = fb.Where(f => f.TimeSaved != null).ToList();
        var stars = new int[5];
        foreach (var f in fb) if (f.Rating is >= 1 and <= 5) stars[f.Rating - 1]++;

        return new FeedbackStats
        {
            Responses = fb.Count,
            AverageRating = fb.Count == 0 ? null : fb.Average(f => f.Rating),
            FeeAnswers = fee.Count,
            FeeYes = fee.Count(f => f.FeeReasonable == true),
            TimeAnswers = time.Count,
            AverageMinutesSaved = time.Count == 0 ? null
                : time.Average(f => TimeSaved.Options.First(o => o.Code == f.TimeSaved).Minutes),
            TimeSavedCounts = TimeSaved.Options.Select(o => (o.Text, time.Count(f => f.TimeSaved == o.Code))).ToList(),
            Stars = stars,

            ProblemsLast7Days = problems.Count(p => p.At >= now.AddDays(-7)),
            ProblemsPerWeekLast4Weeks = problems.Count(p => p.At >= now.AddDays(-28)) / 4.0,
            ProblemsPerWeekBefore = before,
            ProblemKindsLast30Days = Enum.GetValues<ProblemKind>()
                .Select(k => (ProblemText.Label(k), problems.Count(p => p.Kind == k)))
                .Where(x => x.Item2 > 0).ToList(),

            SeparationRating = survey?.SeparationRating,
            OwnerSurveyAt = survey?.CreatedAt
        };
    }
}
