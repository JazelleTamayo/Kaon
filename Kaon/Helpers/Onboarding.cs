using Kaon.Data;
using Kaon.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// A new carinderia and its owner, ready to use: its own link and QR code, an empty menu,
// and the free trial month. Used when an owner signs up on their own.
public static class Onboarding
{
    public static async Task<(StoreSettings Store, AppUser Owner)> CreateAsync(
        KaonDbContext db, IPasswordHasher<AppUser> hasher,
        string carinderiaName, string ownerName, string email, string? phone, string password)
    {
        var name = carinderiaName.Trim();
        var store = new StoreSettings
        {
            Name = name,
            Slug = await StoreContext.UniqueSlugAsync(db, name),
            Hours = null,
            GCashName = null,
            GCashNumber = null,
            DeliveryFee = 30,
            StarterPackagesAdded = true,   // no sample dishes unless the owner asks for them
            DailyMenuSeeded = true
        };
        db.StoreSettings.Add(store);
        await db.SaveChangesAsync();

        var owner = new AppUser
        {
            FullName = ownerName.Trim(),
            Email = email.Trim().ToLower(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = "Owner",
            StoreId = store.Id
        };
        owner.PasswordHash = hasher.HashPassword(owner, password);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        // Module 9: every new carinderia starts with the free trial month
        db.Subscriptions.Add(Billing.NewFor(owner.Id, await db.PlatformSettings.FirstAsync(), DateTime.UtcNow));
        AdminLog.System(db, "A new carinderia signed up: " + store.Name, store);
        await db.SaveChangesAsync();
        return (store, owner);
    }
}
