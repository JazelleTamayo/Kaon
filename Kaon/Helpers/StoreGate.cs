using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// Can customers order from this carinderia right now?
// Orders go to the owner's dashboard. If no active owner can open it (their free month or paid
// period has ended, or their account is switched off), taking orders would lose them. So the
// store shows as closed to customers until an owner can handle orders again.
public static class StoreGate
{
    public static async Task<bool> IsPausedAsync(KaonDbContext db)
    {
        var now = DateTime.UtcNow;
        var ownerIds = await db.Users.AsNoTracking()
            .Where(u => u.Role == "Owner" && u.IsActive)
            .Select(u => u.Id).ToListAsync();
        if (ownerIds.Count == 0) return true;

        var subs = await db.Subscriptions.AsNoTracking()
            .Where(s => ownerIds.Contains(s.OwnerId)).ToListAsync();

        // An owner without a subscription record yet is not blocked
        return !ownerIds.Any(id => subs.FirstOrDefault(s => s.OwnerId == id) is not { } s || s.HasAccess(now));
    }

    // The store as a CUSTOMER should see it: shown as closed when ordering is paused.
    // (The copy is read-only and never saved, so the owner's own Open/Closed switch is untouched.)
    public static async Task<StoreSettings> ForCustomersAsync(KaonDbContext db)
    {
        var store = await db.StoreSettings.AsNoTracking().FirstAsync();
        if (await IsPausedAsync(db))
        {
            store.IsOpen = false;
            store.PausedByBilling = true;
        }
        return store;
    }
}
