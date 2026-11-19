using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 2: Carinderia store setup
[Authorize(Roles = "Owner")]
public class OwnerSettingsController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;

    public OwnerSettingsController(KaonDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _db.StoreSettings.AsNoTracking().FirstAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(StoreSettings form, IFormFile? qrFile, bool removeQr)
    {
        if (!form.PickupEnabled && !form.DeliveryEnabled)
            ModelState.AddModelError("", "Turn on at least one: pickup or home delivery.");
        if (!form.AcceptsCash && !form.AcceptsCashless)
            ModelState.AddModelError("", "Accept at least one payment type: cash or GCash.");
        var current = await _db.StoreSettings.FirstAsync();
        var willHaveQr = (qrFile is { Length: > 0 }) || (!removeQr && !string.IsNullOrEmpty(current.PaymentQr));
        if (form.AcceptsCashless && string.IsNullOrWhiteSpace(form.GCashNumber) &&
            string.IsNullOrWhiteSpace(form.BankAccountNumber) && !willHaveQr)
            ModelState.AddModelError("", "To accept payment in advance, add a GCash number, a bank account, or a QR code.");
        if (!string.IsNullOrWhiteSpace(form.BankAccountNumber) && string.IsNullOrWhiteSpace(form.BankName))
            ModelState.AddModelError(nameof(form.BankName), "Enter the bank or e-wallet name.");

        string? newQr = null;
        if (ModelState.IsValid && qrFile is { Length: > 0 })
        {
            var (name, error) = await ProofStorage.SaveAsync(qrFile, _env, "qr");
            if (error != null) ModelState.AddModelError("qrFile", error);
            else newQr = name;
        }
        if (!ModelState.IsValid)
        {
            form.PaymentQr = current.PaymentQr;
            return View(form);
        }

        var s = current;
        if (newQr != null) { ProofStorage.Delete(_env, s.PaymentQr, "qr"); s.PaymentQr = newQr; }
        else if (removeQr) { ProofStorage.Delete(_env, s.PaymentQr, "qr"); s.PaymentQr = null; }
        s.BankName = form.BankName?.Trim();
        s.BankAccountName = form.BankAccountName?.Trim();
        s.BankAccountNumber = form.BankAccountNumber?.Trim();
        s.Name = form.Name.Trim();
        s.Hours = form.Hours?.Trim();
        s.Phone = form.Phone?.Trim();
        s.DeliveryArea = form.DeliveryArea?.Trim();
        s.DeliveryFee = form.DeliveryFee;
        s.PickupEnabled = form.PickupEnabled;
        s.DeliveryEnabled = form.DeliveryEnabled;
        s.IsOpen = form.IsOpen;
        s.AcceptsCash = form.AcceptsCash;
        s.AcceptsCashless = form.AcceptsCashless;
        s.GCashName = form.GCashName?.Trim();
        s.GCashNumber = form.GCashNumber?.Trim();
        s.PaymentNote = form.PaymentNote?.Trim();
        await _db.SaveChangesAsync();

        TempData["Saved"] = "Store settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
