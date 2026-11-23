using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// A carinderia has one plan, even when it has more than one owner.
// The first owner gets the free trial. An owner added later to the same carinderia shares
// that carinderia's dates, so adding an owner never starts another free month.
public static class SubscriptionStart
{
    public static async Task<Subscription> ForOwnerAsync(KaonDbContext db, int ownerId, int? storeId, DateTime nowUtc)
    {
        var platform = await db.PlatformSettings.AsNoTracking().FirstAsync();
        var sub = Billing.NewFor(ownerId, platform, nowUtc);
        if (storeId == null) return sub;

        var coOwnerIds = await db.Users.AsNoTracking()
            .Where(u => u.Role == "Owner" && u.StoreId == storeId && u.Id != ownerId)
            .Select(u => u.Id)
            .ToListAsync();
        if (coOwnerIds.Count == 0) return sub;

        var existing = await db.Subscriptions.AsNoTracking()
            .Where(s => coOwnerIds.Contains(s.OwnerId))
            .ToListAsync();

        // The co-owner with the longest access decides the carinderia's plan (same rule as the admin overview)
        var best = existing.OrderByDescending(s => s.AccessUntil).FirstOrDefault();
        if (best != null)
        {
            sub.TrialEndsAt = best.TrialEndsAt;
            sub.PaidUntil = best.PaidUntil;
        }
        return sub;
    }

    // After a payment or free days for one owner, the carinderia's other owners get the same dates,
    // so every owner of an open carinderia can use the order board. Call before SaveChanges.
    public static async Task SyncCoOwnersAsync(KaonDbContext db, Subscription sub)
    {
        var storeId = await db.Users.AsNoTracking().Where(u => u.Id == sub.OwnerId).Select(u => u.StoreId).FirstOrDefaultAsync();
        if (storeId == null) return;

        var coOwnerIds = await db.Users.AsNoTracking()
            .Where(u => u.Role == "Owner" && u.StoreId == storeId && u.Id != sub.OwnerId)
            .Select(u => u.Id)
            .ToListAsync();
        if (coOwnerIds.Count == 0) return;

        var others = await db.Subscriptions.Where(s => coOwnerIds.Contains(s.OwnerId)).ToListAsync();
        foreach (var o in others)
        {
            // Never shorten anyone's access
            if (sub.TrialEndsAt > o.TrialEndsAt) o.TrialEndsAt = sub.TrialEndsAt;
            if (sub.PaidUntil != null && (o.PaidUntil == null || sub.PaidUntil > o.PaidUntil)) o.PaidUntil = sub.PaidUntil;
        }
    }
}
