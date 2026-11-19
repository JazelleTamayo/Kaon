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

    public AdminBillingController(KaonDbContext db, IEmailSender email)
    {
        _db = db;
        _email = email;
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
            await _db.SaveChangesAsync();
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
            await _db.SaveChangesAsync();
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

    private async Task<Subscription?> GetOrCreate(int ownerId)
    {
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerId && u.Role == "Owner");
        if (owner == null) return null;

        var sub = await _db.Subscriptions.Include(s => s.Owner).FirstOrDefaultAsync(s => s.OwnerId == ownerId);
        if (sub != null) return sub;

        sub = Billing.NewFor(ownerId, await _db.PlatformSettings.AsNoTracking().FirstAsync(), DateTime.UtcNow);
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        sub.Owner = owner;
        return sub;
    }
}
