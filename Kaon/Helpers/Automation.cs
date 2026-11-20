using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// The routine work Kaon does by itself, so nobody has to remember it:
//  - plan reminders by email: 7, 3 and 1 day before a plan ends, and once when it has ended
//  - each morning, the dishes of the last open day are posted again (the owner can switch this off)
//  - old, unused invitation and reset links are cleaned out
public static class Automation
{
    public record Result(int Reminders, int MenusPosted, int LinksCleaned);

    public static async Task<Result> RunOnceAsync(KaonDbContext db, IEmailSender email, string baseUrl, DateTime nowUtc, DateTime today)
    {
        var reminders = await RemindersAsync(db, email, baseUrl, nowUtc);
        var menus = await PostDailyMenusAsync(db, nowUtc, today);
        var cleaned = await CleanLinksAsync(db, nowUtc);
        return new Result(reminders, menus, cleaned);
    }

    // ---------- reminders ----------
    // 7 -> 3 -> 1 days before the end, then -1 once it has ended. A higher rank is more urgent.
    private static int Rank(int stage) => stage switch { 7 => 1, 3 => 2, 1 => 3, -1 => 4, _ => 0 };

    private static int StageFor(Subscription sub, DateTime now)
    {
        if (now >= sub.AccessUntil) return -1;
        var left = sub.DaysLeft(now);
        return left <= 1 ? 1 : left <= 3 ? 3 : left <= Subscription.ReminderDays ? 7 : 0;
    }

    public static async Task<int> RemindersAsync(KaonDbContext db, IEmailSender email, string baseUrl, DateTime now)
    {
        var sent = 0;
        var subs = await db.Subscriptions.Include(s => s.Owner).ToListAsync();
        foreach (var sub in subs)
        {
            if (sub.Owner is not { IsActive: true, Role: "Owner" }) continue;

            // A new end date (a payment, free days) starts the reminders over
            if (sub.ReminderForAccessUntil != sub.AccessUntil)
            {
                sub.ReminderStage = 0;
                sub.ReminderForAccessUntil = sub.AccessUntil;
            }

            var stage = StageFor(sub, now);
            if (stage == 0 || Rank(stage) <= Rank(sub.ReminderStage)) continue;

            var days = sub.DaysLeft(now);
            var message = stage == -1
                ? "your Kaon subscription has ended. Renew to open your orders, menu and store again."
                : "your Kaon subscription ends in " + days + " day" + (days == 1 ? "" : "s") + " (" + sub.AccessUntil.ToLocalTime().ToString("MMM d, yyyy") + "). Renew now so your orders keep coming in.";
            var link = baseUrl.TrimEnd('/') + "/Billing";

            var result = await email.SendAsync(sub.Owner.Email, "Your Kaon subscription",
                EmailTemplates.SubscriptionReminder(sub.Owner.FullName, message, days, link));
            if (result == null) continue;   // email not set up yet: try again on the next run

            sub.ReminderStage = stage;
            sub.LastReminderAt = now;
            sent++;
        }
        await db.SaveChangesAsync();
        return sent;
    }

    // ---------- the daily menu ----------
    public static async Task<int> PostDailyMenusAsync(KaonDbContext db, DateTime now, DateTime today)
    {
        var posted = 0;
        var stores = await db.StoreSettings.Where(s => s.AutoCopyMenu && s.IsOpen && !s.SuspendedByAdmin && s.LastAutoMenuOn != today).ToListAsync();
        foreach (var store in stores)
        {
            // Only while an owner can handle orders (the plan is active)
            if (await StoreGate.IsPausedAsync(db, store.Id)) continue;

            store.LastAutoMenuOn = today;   // once a day, so dishes the owner takes off later stay off

            var hasToday = await db.DailyMenuEntries.AnyAsync(e => e.Date == today && e.MenuItem!.StoreId == store.Id);
            if (hasToday) continue;

            var lastDay = await db.DailyMenuEntries.Where(e => e.Date < today && e.MenuItem!.StoreId == store.Id)
                .MaxAsync(e => (DateTime?)e.Date);
            if (lastDay == null) continue;

            var dishes = await db.DailyMenuEntries.Include(e => e.MenuItem)
                .Where(e => e.Date == lastDay && e.MenuItem!.StoreId == store.Id && e.MenuItem.Category != MenuItem.PackageCategory && e.MenuItem.IsAvailable)
                .Select(e => e.MenuItemId).Distinct().ToListAsync();
            // Portions are not copied: yesterday's number is what was left. The owner sets them if they limit portions.
            foreach (var id in dishes)
                db.DailyMenuEntries.Add(new DailyMenuEntry { MenuItemId = id, Date = today });
            if (dishes.Count > 0) posted++;
        }
        await db.SaveChangesAsync();
        return posted;
    }

    // ---------- housekeeping ----------
    public static async Task<int> CleanLinksAsync(KaonDbContext db, DateTime now)
    {
        var old = await db.PasswordTokens.Where(t => t.ExpiresAt < now.AddDays(-14) || (t.UsedAt != null && t.UsedAt < now.AddDays(-14))).ToListAsync();
        db.PasswordTokens.RemoveRange(old);
        await db.SaveChangesAsync();
        return old.Count;
    }
}

// Short email notices to people (never anything private about customers)
public static class Notify
{
    public static async Task ToAsync(IEmailSender email, ILogger log, string to, string subject, string heading, string body)
    {
        try { await email.SendAsync(to, subject, EmailTemplates.Notice(heading, body)); }
        catch (Exception ex) { log.LogWarning(ex, "Could not send a notice email."); }
    }

    public static async Task AdminsAsync(KaonDbContext db, IEmailSender email, ILogger log, string subject, string heading, string body)
    {
        var admins = await db.Users.AsNoTracking().Where(u => u.Role == "Admin" && u.IsActive && u.PasswordHash != "").Select(u => u.Email).ToListAsync();
        foreach (var to in admins) await ToAsync(email, log, to, subject, heading, body);
    }
}
