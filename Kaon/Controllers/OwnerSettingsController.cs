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
    private readonly StoreContext _stores;

    public OwnerSettingsController(KaonDbContext db, IWebHostEnvironment env, StoreContext stores)
    {
        _db = db;
        _env = env;
        _stores = stores;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await FillPanels();
        return View(await _stores.StoreAsync());
    }

    // The carinderia's own link, the one the QR code opens
    private async Task<string> OwnLinkAsync()
    {
        var store = await _stores.StoreAsync();
        return Url.Action("Landing", "Store", new { slug = store.Slug }, Request.Scheme)!;
    }

    // The QR code picture for THIS carinderia. It opens /q/{slug}, which counts the scan (Module 12),
    // and says which printout it was on (s=stand, flyer, ...).
    [HttpGet]
    public async Task<IActionResult> QrImage(bool download = false, string? s = null)
    {
        var store = await _stores.StoreAsync();
        var png = QrCodes.Png(Url.Action("Scan", "Store", new { slug = store.Slug, s = QrSources.Clean(s) }, Request.Scheme)!);

        if (download) Response.Headers.ContentDisposition = "attachment; filename=\"" + store.Slug + "-qr.png\"";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(png, "image/png");
    }

    // Module 12: posters, table stands, counter signs and takeaway flyers, ready to print
    [HttpGet]
    public async Task<IActionResult> Print(string kind = "poster")
    {
        var store = await _stores.StoreAsync();
        kind = QrSources.Printables.Any(p => p.Key == kind) ? kind : "poster";
        return View("~/Views/Shared/Printable.cshtml", new PrintableVm
        {
            Store = store,
            Kind = kind,
            QrUrl = Url.Action("QrImage", new { s = kind })!,
            Link = await OwnLinkAsync(),
            ScanUrl = Url.Action("Scan", "Store", new { slug = store.Slug, s = kind }, Request.Scheme)!,
            BackUrl = Url.Action("Index", null, null, null, "share")!,
            BackText = "Back to my store"
        });
    }

    // QR scan counts (Module 12) and saved riders (Module 7) shown beside the store form
    private async Task FillPanels()
    {
        var sid = await _stores.RequireAsync();
        ViewBag.Qr = await QrCodes.StatsAsync(_db, sid);
        ViewBag.Riders = await _db.Riders.AsNoTracking().Where(r => r.StoreId == sid && r.IsActive).OrderBy(r => r.Name).ToListAsync();
    }

    // ---------- Module 7: the carinderia's delivery riders ----------
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRider(string? name, string? phone)
    {
        var sid = await _stores.RequireAsync();
        name = name?.Trim(); phone = phone?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(phone) || name.Length > 60 || phone.Length > 20)
        {
            TempData["Warn"] = "Enter the rider's name and mobile number.";
        }
        else if (await _db.Riders.AnyAsync(r => r.StoreId == sid && r.IsActive && r.Phone == phone))
        {
            TempData["Warn"] = "A rider with that number is already on your list.";
        }
        else
        {
            _db.Riders.Add(new Rider { StoreId = sid, Name = name, Phone = phone });
            await _db.SaveChangesAsync();
            TempData["Saved"] = name + " is on your rider list.";
        }
        return Redirect(Url.Action(nameof(Index))! + "#riders");
    }

    // Orders already given to this rider keep their name, number and link
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRider(int id)
    {
        var sid = await _stores.RequireAsync();
        var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == id && r.StoreId == sid);
        if (rider != null)
        {
            rider.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Saved"] = rider.Name + " was removed from your rider list.";
        }
        return Redirect(Url.Action(nameof(Index))! + "#riders");
    }

    // The old poster address still works
    [HttpGet]
    public IActionResult Poster() => RedirectToAction(nameof(Print), new { kind = "poster" });

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
            await FillPanels();
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
        s.AutoCopyMenu = form.AutoCopyMenu;
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
