using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Kaon.Controllers;

// Module 2: Carinderia store setup
[Authorize(Roles = "Owner")]
public class OwnerSettingsController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly StoreContext _stores;

    public OwnerSettingsController(KaonDbContext db, IWebHostEnvironment env, StoreContext stores)
    {
        _db = db;
        _env = env;
        _stores = stores;
    }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _stores.StoreAsync());

    // The carinderia's own link, the one the QR code opens
    private async Task<string> OwnLinkAsync()
    {
        var store = await _stores.StoreAsync();
        return Url.Action("Landing", "Store", new { slug = store.Slug }, Request.Scheme)!;
    }

    // The QR code picture for THIS carinderia's link (made here; the link is never sent to another website)
    [HttpGet]
    public async Task<IActionResult> QrImage(bool download = false)
    {
        var link = await OwnLinkAsync();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(20);

        var store = await _stores.StoreAsync();
        if (download) Response.Headers.ContentDisposition = "attachment; filename=\"" + store.Slug + "-qr.png\"";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(png, "image/png");
    }

    // A poster the owner can print and put on the counter
    [HttpGet]
    public async Task<IActionResult> Poster()
    {
        ViewBag.Link = await OwnLinkAsync();
        return View(await _stores.StoreAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(StoreSettings form, IFormFile? qrFile, bool removeQr)
    {
        if (!form.PickupEnabled && !form.DeliveryEnabled)
            ModelState.AddModelError("", "Turn on at least one: pickup or home delivery.");
        if (!form.AcceptsCash && !form.AcceptsCashless)
            ModelState.AddModelError("", "Accept at least one payment type: cash or GCash.");
        var current = await _stores.StoreAsync(track: true);
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
            form.Id = current.Id;
            form.Slug = current.Slug;
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
