using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 9 (owner side): see the subscription, pay Kaon by GCash and upload proof.
// The Kaon admin confirms the payment (see AdminBillingController).
[Authorize]
public class BillingController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;

    private readonly IEmailSender _email;
    private readonly ILogger<BillingController> _log;

    public BillingController(KaonDbContext db, IWebHostEnvironment env, IEmailSender email, ILogger<BillingController> log)
    {
        _db = db;
        _env = env;
        _email = email;
        _log = log;
    }

    private int MyId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Subscription> MySubscription()
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.OwnerId == MyId);
        if (sub != null) return sub;

        // Owners created before billing existed get their free month the first time they open this page
        // A second owner of a carinderia shares its plan instead of starting another free month
        var storeId = await _db.Users.AsNoTracking().Where(u => u.Id == MyId).Select(u => u.StoreId).FirstOrDefaultAsync();
        sub = await SubscriptionStart.ForOwnerAsync(_db, MyId, storeId, DateTime.UtcNow);
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    private async Task<BillingVm> BuildVm(Subscription sub) => new()
    {
        Subscription = sub,
        Platform = await _db.PlatformSettings.AsNoTracking().FirstAsync(),
        Payments = await _db.SubscriptionPayments.AsNoTracking()
            .Where(p => p.SubscriptionId == sub.Id).OrderByDescending(p => p.SubmittedAt).ToListAsync()
    };

    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Index() => View(await BuildVm(await MySubscription()));

    [HttpPost, Authorize(Roles = "Owner"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitPaymentForm form, IFormFile? proof)
    {
        var sub = await MySubscription();
        var platform = await _db.PlatformSettings.AsNoTracking().FirstAsync();

        if (proof == null || proof.Length == 0)
            ModelState.AddModelError("proof", "Please upload a screenshot of your GCash payment.");
        if (await _db.SubscriptionPayments.AnyAsync(p => p.SubscriptionId == sub.Id && p.Status == SubscriptionPaymentStatus.Pending))
            ModelState.AddModelError("", "You already sent a payment. Please wait for the Kaon team to confirm it.");

        string? file = null;
        if (ModelState.IsValid)
        {
            var (name, error) = await ProofStorage.SaveAsync(proof!, _env, "billing");
            if (error != null) ModelState.AddModelError("proof", error);
            else file = name;
        }
        if (!ModelState.IsValid)
        {
            var vm = await BuildVm(sub);
            ViewBag.Form = form;
            return View("Index", vm);
        }

        _db.SubscriptionPayments.Add(new SubscriptionPayment
        {
            SubscriptionId = sub.Id,
            Months = form.Months,
            Amount = platform.MonthlyFee * form.Months,
            Reference = string.IsNullOrWhiteSpace(form.Reference) ? null : form.Reference.Trim(),
            Proof = file
        });
        await _db.SaveChangesAsync();
        // Automatic: the Kaon team is told a payment is waiting
        var who = await _db.Users.AsNoTracking().Where(u => u.Id == MyId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "An owner";
        await Notify.AdminsAsync(_db, _email, _log, "A Kaon payment is waiting", "A payment is waiting to be confirmed",
            who + " sent a subscription payment. Open Billing in the admin area to confirm it.");
        TempData["Saved"] = "Thank you! We got your payment. The Kaon team will confirm it soon.";
        return RedirectToAction(nameof(Index));
    }

    // Subscription proofs are private: only the owner who sent it and the Kaon admin can open one.
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Proof(int id)
    {
        var payment = await _db.SubscriptionPayments.AsNoTracking()
            .Include(p => p.Subscription).FirstOrDefaultAsync(p => p.Id == id);
        if (payment?.Proof == null) return NotFound();
        if (!User.IsInRole("Admin") && payment.Subscription!.OwnerId != MyId) return NotFound();

        var path = ProofStorage.PathFor(_env, payment.Proof, "billing");
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, ProofStorage.ContentType(payment.Proof));
    }
}
