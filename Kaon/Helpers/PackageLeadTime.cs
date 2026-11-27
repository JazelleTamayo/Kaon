using Kaon.Models;

namespace Kaon.Helpers;

// How many days ahead a food package must be ordered, by its price.
// Bigger/pricier packages need more notice so the owner can source ingredients.
public static class PackageLeadTime
{
    public static int DaysFor(decimal price) => price switch
    {
        >= 5000m => 10,
        >= 1000m => 5,
        _ => 3
    };

    // The lead time this order's biggest item required when it was placed
    public static int LeadDaysFor(Order o) => o.Items.Count == 0 ? 0 : o.Items.Max(i => DaysFor(i.UnitPrice));

    // True once a reserved package is close enough to its pickup/delivery date that the
    // owner should start thinking about it. A bigger lead time means it flags earlier.
    public static bool IsPrepareSoon(Order o) =>
        o.ScheduledFor != null &&
        (o.ScheduledFor.Value.Date - DateTime.Today).TotalDays <= Math.Max(1, LeadDaysFor(o) / 2);
}
