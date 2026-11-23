using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 9 (Kaon admin side): fee and trial settings, confirming GCash payments,
// extending a trial, and reminding owners whose subscription is about to end.
[Authorize(Roles = "Admin")]
public class AdminBillingController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<AdminBillingController> _log;
    private readonly IConfiguration _config;

    public AdminBillingController(KaonDbContext db, IEmailSender email, ILogger<AdminBillingController> log, IConfiguration config)
    {
        _db = db;
        _email = email;
        _log = log;
        _config = config;
    }

    // The same routine work the app does by itself every 15 minutes, on demand
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAutomation()
    {
        var baseUrl = _config["Kaon:PublicUrl"] ?? (Request.Scheme + "://" + Request.Host);
        var r = await Automation.RunOnceAsync(_db, _email, baseUrl, DateTime.UtcNow, DateTime.Today);
        AdminLog.Add(_db, User, "Ran the automatic checks (" + r.Reminders + " reminders sent, " + r.MenusPosted + " menus posted)");
        await _db.SaveChangesAsync();
        TempData["Saved"] = "Checked. " + r.Reminders + " reminder(s) sent, " + r.MenusPosted + " daily menu(s) posted.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Index()
    {
        var owners = await _db.Users.AsNoTracking().Where(u => u.Role == "Owner").OrderBy(u => u.FullName).ToListAsync();
        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync();
        var payments = await _db.SubscriptionPayments.AsNoTracking()
            .Include(p => p.Subscription).ThenInclude(s => s!.Owner)
            .OrderByDescending(p => p.SubmittedAt).ToListAsync();

        return View(new AdminBillingVm
        {
            Platform = await _db.PlatformSettings.AsNoTracking().FirstAsync(),
            StoreNames = await _db.StoreSettings.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name),
            Rows = owners.Select(o =>
            {
                var s = subs.FirstOrDefault(x => x.OwnerId == o.Id);
                return new AdminBillingRow
                {
                    Owner = o,
                    Subscription = s,
                    PendingCount = s == null ? 0 : payments.Count(p => p.SubscriptionId == s.Id && p.Status == SubscriptionPaymentStatus.Pending)
                };
            }).ToList(),
            PendingPayments = payments.Where(p => p.Status == SubscriptionPaymentStatus.Pending).OrderBy(p => p.SubmittedAt).ToList(),
            RecentPayments = payments.Where(p => p.Status != SubscriptionPaymentStatus.Pending).Take(15).ToList(),
            ConfirmedTotal = payments.Where(p => p.Status == SubscriptionPaymentStatus.Confirmed).Sum(p => p.Amount)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Settings() => View(await _db.PlatformSettings.AsNoTracking().FirstAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(PlatformSettings form)
    {
        if (!ModelState.IsValid) return View(form);

        var s = await _db.PlatformSettings.FirstAsync();
        s.MonthlyFee = form.MonthlyFee;
        s.TrialDays = form.TrialDays;
        s.GCashName = form.GCashName?.Trim();
        s.GCashNumber = form.GCashNumber?.Trim();
        s.PaymentNote = form.PaymentNote?.Trim();
        AdminLog.Add(_db, User, "Changed the billing settings (fee, free days or GCash details)");
        await _db.SaveChangesAsync();
        TempData["Saved"] = "Billing settings saved. The new fee applies to payments sent from now on.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        var p = await _db.SubscriptionPayments.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == id);
        if (p is { Status: SubscriptionPaymentStatus.Pending })
        {
            Billing.Confirm(p.Subscription!, p, DateTime.UtcNow);
            await SubscriptionStart.SyncCoOwnersAsync(_db, p.Subscription!);
            AdminLog.Add(_db, User, "Confirmed a payment of ₱" + p.Amount.ToString("N0") + " (" + p.Months + " mo)", await StoreOfSubscription(p.Subscription!.OwnerId));
            await _db.SaveChangesAsync();
            await Notify.ToAsync(_email, _log, p.Subscription!.Owner?.Email ?? (await _db.Users.AsNoTracking().Where(u => u.Id == p.Subscription.OwnerId).Select(u => u.Email).FirstAsync()),
                "Your Kaon payment is confirmed", "Payment confirmed",
                "Thank you! Your payment is confirmed. Your Kaon access now runs until " + p.PeriodEnd!.Value.ToLocalTime().ToString("MMMM d, yyyy") + ".");
            TempData["Saved"] = "Payment confirmed. Access now runs until " + p.PeriodEnd!.Value.ToLocalTime().ToString("MMM d, yyyy") + ".";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? note)
    {
        var p = await _db.SubscriptionPayments.FirstOrDefaultAsync(x => x.Id == id);
        if (p is { Status: SubscriptionPaymentStatus.Pending })
        {
            p.Status = SubscriptionPaymentStatus.Rejected;
            p.ReviewedAt = DateTime.UtcNow;
            p.ReviewNote = string.IsNullOrWhiteSpace(note) ? "Payment could not be verified." : note.Trim();
            var rejSub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.SubscriptionId);
            AdminLog.Add(_db, User, "Rejected a payment of ₱" + p.Amount.ToString("N0"), rejSub == null ? null : await StoreOfSubscription(rejSub.OwnerId));
            await _db.SaveChangesAsync();
            var rejOwner = await _db.Users.AsNoTracking().Where(u => u.Id == rejSub!.OwnerId).Select(u => u.Email).FirstOrDefaultAsync();
            if (rejOwner != null)
                await Notify.ToAsync(_email, _log, rejOwner, "About your Kaon payment", "We could not confirm your payment",
                    p.ReviewNote + " Please send the payment again from the Subscription page.");
            TempData["Saved"] = "Payment rejected. The owner can send it again.";
        }
        return RedirectToAction(nameof(Index));
    }

    // A payment handed over in person: no screenshot, confirmed right away
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(int ownerId, int months)
    {
        var sub = await GetOrCreate(ownerId);
        if (sub == null || months < 1 || months > Billing.MaxMonths) return RedirectToAction(nameof(Index));

        var platform = await _db.PlatformSettings.AsNoTracking().FirstAsync();
        var p = new SubscriptionPayment
        {
            SubscriptionId = sub.Id,
            Months = months,
            Amount = platform.MonthlyFee * months,
            RecordedByAdmin = true,
            ReviewNote = "Recorded by the Kaon team"
        };
        _db.SubscriptionPayments.Add(p);
        Billing.Confirm(sub, p, DateTime.UtcNow);
        await SubscriptionStart.SyncCoOwnersAsync(_db, sub);
        AdminLog.Add(_db, User, "Recorded a payment in person (" + months + " mo, ₱" + p.Amount.ToString("N0") + ")", await StoreOfSubscription(ownerId));
        await _db.SaveChangesAsync();
        TempData["Saved"] = "Recorded " + months + " month(s) for " + sub.Owner!.FullName + ".";
        return RedirectToAction(nameof(Index));
    }

    // Give an owner more free days (for example to finish the pilot month)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendTrial(int ownerId, int days)
    {
        var sub = await GetOrCreate(ownerId);
        if (sub == null || days < 1 || days > 90) return RedirectToAction(nameof(Index));

        var now = DateTime.UtcNow;
        sub.TrialEndsAt = (sub.TrialEndsAt > now ? sub.TrialEndsAt : now).AddDays(days);
        await SubscriptionStart.SyncCoOwnersAsync(_db, sub);
        AdminLog.Add(_db, User, "Added " + days + " free days", await StoreOfSubscription(ownerId));
        await _db.SaveChangesAsync();
        TempData["Saved"] = sub.Owner!.FullName + " now has free access until " + sub.TrialEndsAt.ToLocalTime().ToString("MMM d, yyyy") + ".";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remind(int ownerId)
    {
        var sub = await GetOrCreate(ownerId);
        if (sub == null) return RedirectToAction(nameof(Index));

        var now = DateTime.UtcNow;
        var days = sub.DaysLeft(now);
        var message = days > 0
            ? "your Kaon subscription ends in " + days + " day" + (days == 1 ? "" : "s") + " (" + sub.AccessUntil.ToLocalTime().ToString("MMM d, yyyy") + "). Renew now so your orders keep coming in."
            : "your Kaon subscription has ended. Renew to open your orders, menu and store again.";
        var link = Url.Action("Index", "Billing", null, Request.Scheme)!;
        var result = await _email.SendAsync(sub.Owner!.Email, "Your Kaon subscription",
            EmailTemplates.SubscriptionReminder(sub.Owner.FullName, message, days, link));
        if (result != null) { sub.LastReminderAt = now; await _db.SaveChangesAsync(); }
        TempData["Saved"] = result == null
            ? "Email is not set up, so the reminder was not sent."
            : "Reminder " + (result == "outbox" ? "saved to the test outbox" : "emailed") + " for " + sub.Owner.FullName + ".";
        return RedirectToAction(nameof(Index));
    }

    // The carinderia an owner runs, for the activity log
    private async Task<StoreSettings?> StoreOfSubscription(int ownerId)
    {
        var storeId = await _db.Users.AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.StoreId).FirstOrDefaultAsync();
        return storeId == null ? null : await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == storeId);
    }

    private async Task<Subscription?> GetOrCreate(int ownerId)
    {
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId && u.Role == "Owner");
        if (owner == null) return null;

        var sub = await _db.Subscriptions.Include(s => s.Owner).FirstOrDefaultAsync(s => s.OwnerId == ownerId);
        if (sub != null) return sub;

        sub = await SubscriptionStart.ForOwnerAsync(_db, ownerId, owner.StoreId, DateTime.UtcNow);
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        sub.Owner = owner;
        return sub;
    }
}
