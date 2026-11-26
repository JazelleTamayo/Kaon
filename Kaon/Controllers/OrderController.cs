using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

[Authorize(Roles = "Customer")]
public class OrderController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly StoreContext _stores;

    public OrderController(KaonDbContext db, IWebHostEnvironment env, StoreContext stores)
    {
        _db = db;
        _env = env;
        _stores = stores;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // My orders, newest first
    public async Task<IActionResult> Index()
    {
        var uid = CurrentUserId;
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == uid)
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .ToListAsync();
        // One account, several carinderias: each order says where it was placed
        var storeIds = orders.Select(o => o.StoreId).Distinct().ToList();
        ViewBag.StoreNames = await _db.StoreSettings.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
        return View(orders);
    }

    private async Task FillCheckoutView()
    {
        var store = await StoreGate.ForCustomersAsync(_db, await _stores.RequireAsync());
        ViewBag.Cart = CartHelper.Get(HttpContext.Session);
        ViewBag.Store = store;
        ViewBag.DeliveryFee = store.DeliveryFee;
        ViewBag.HasPackage = ((List<CartLine>)ViewBag.Cart).Any(c => c.IsPackage);
    }

    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var cart = CartHelper.Get(HttpContext.Session);
        if (!cart.Any()) return RedirectToAction("Index", "Menu");

        var user = await _db.Users.FindAsync(CurrentUserId);
        await FillCheckoutView();
        var store = (StoreSettings)ViewBag.Store;

        return View(new CheckoutForm
        {
            CustomerName = user?.FullName ?? "",
            Phone = user?.Phone ?? "",
            Address = user?.Address,
            OrderType = store.PickupEnabled ? OrderType.Pickup : OrderType.Delivery,
            PaymentMethod = store.AcceptsCash ? PaymentMethod.Cash : PaymentMethod.Cashless,
            // Food packages: the date was already chosen when it was added to the cart
            ScheduledFor = cart.FirstOrDefault(c => c.IsPackage)?.ScheduledFor
        });
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutForm form)
    {
        var cart = CartHelper.Get(HttpContext.Session);
        if (!cart.Any()) return RedirectToAction("Index", "Menu");

        var sid = await _stores.RequireAsync();
        var store = await StoreGate.ForCustomersAsync(_db, sid);

        if (!store.IsOpen)
            ModelState.AddModelError("", "Sorry, " + store.Name + " is closed right now. Please try again later.");
        if (form.OrderType == OrderType.Pickup && !store.PickupEnabled)
            ModelState.AddModelError("", "Pickup is not available right now.");
        if (form.OrderType == OrderType.Delivery && !store.DeliveryEnabled)
            ModelState.AddModelError("", "Home delivery is not available right now.");
        if (form.OrderType == OrderType.Delivery && string.IsNullOrWhiteSpace(form.Address))
            ModelState.AddModelError(nameof(form.Address), "Please enter your delivery address.");

        // Payment: only what the carinderia accepts
        var cashlessOk = store.CashlessAvailable;
        if (form.PaymentMethod == PaymentMethod.Cash && !store.AcceptsCash)
            ModelState.AddModelError("", "This carinderia does not take cash payments. Please pay in advance.");
        if (form.PaymentMethod == PaymentMethod.Cashless && !cashlessOk)
            ModelState.AddModelError("", "Pay in advance is not available right now. Please choose cash.");

        var ids = cart.Select(c => c.MenuItemId).ToList();
        var menu = await _db.MenuItems
            .Include(m => m.Options)
            .Where(m => m.StoreId == sid && ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        // Daily meals: only orderable if the owner picked them for today's menu.
        // Food packages keep using their own on/off switch.
        var today = DateTime.Today;
        // Grouped first: if a dish was ever put on today's menu twice, checkout still works
        var todayEntries = (await _db.DailyMenuEntries
            .Where(e => e.Date == today && ids.Contains(e.MenuItemId))
            .OrderBy(e => e.Id)
            .ToListAsync())
            .GroupBy(e => e.MenuItemId)
            .ToDictionary(g => g.Key, g => g.First());

        bool CanOrderLine(int menuItemId, MenuItem mi) => mi.IsPackage
            ? mi.CanOrder
            : todayEntries.TryGetValue(menuItemId, out var te) && te.CanOrder;

        var unavailable = cart
            .Where(c => !menu.ContainsKey(c.MenuItemId) || !CanOrderLine(c.MenuItemId, menu[c.MenuItemId]))
            .ToList();
        if (unavailable.Any())
        {
            ModelState.AddModelError("",
                "Sorry, " + string.Join(", ", unavailable.Select(u => u.Name)) +
                " is sold out. Please remove it from your cart.");
        }

        foreach (var line in cart.Except(unavailable))
        {
            var m = menu[line.MenuItemId];
            var stockLeft = m.IsPackage ? null : todayEntries[line.MenuItemId].Stock;
            if (stockLeft != null && line.Quantity > stockLeft)
                ModelState.AddModelError("", "Only " + stockLeft + " " + m.Name + " left. Please lower the quantity in your cart.");
            if (m.Options.Any() && m.Options.All(o => o.Id != line.OptionId))
                ModelState.AddModelError("", "Please remove " + m.Name + " from your cart and add it again with a size.");
        }

        // Sections the carinderia has switched off can't be ordered
        foreach (var line in cart)
        {
            if (!menu.TryGetValue(line.MenuItemId, out var mi)) continue;
            if (mi.IsPackage ? !store.OffersPackages : !store.OffersDailyMenu)
                ModelState.AddModelError("", (mi.IsPackage ? "Food packages are" : "The daily menu is") +
                    " not offered right now. Please remove " + mi.Name + " from your cart.");
        }

        // Food packages are made ahead: the customer must say when they need them,
        // far enough out for the owner to source ingredients for the biggest item ordered.
        var hasPackage = cart.Any(c => menu.TryGetValue(c.MenuItemId, out var mi) && mi.IsPackage);
        decimal? depositAmount = null;
        if (hasPackage)
        {
            var packageLines = cart.Where(c => menu.TryGetValue(c.MenuItemId, out var mi) && mi.IsPackage).ToList();
            var leadDays = packageLines.Count == 0 ? 0 : packageLines.Max(c => PackageLeadTime.DaysFor(c.Price));
            var earliest = DateTime.Today.AddDays(leadDays);

            if (form.ScheduledFor == null)
                ModelState.AddModelError(nameof(form.ScheduledFor), "Please tell us when you need your food package.");
            else if (form.ScheduledFor < earliest)
                ModelState.AddModelError(nameof(form.ScheduledFor), "Food packages need at least " + leadDays + " days' notice.");

            depositAmount = Math.Round(packageLines.Sum(c => c.Price * c.Quantity) * 0.5m, 2);
        }

        // Ordinary meals: an optional preferred time later today (packages use their own date above)
        DateTime? preferredAt = null;
        if (!hasPackage && form.PreferredTime != null)
        {
            preferredAt = DateTime.Today.Add(form.PreferredTime.Value);
            if (preferredAt < DateTime.Now.AddMinutes(-5))
                ModelState.AddModelError(nameof(form.PreferredTime), "That time has passed. Please choose a later time today, or leave it empty.");
        }

        if (!ModelState.IsValid)
        {
            await FillCheckoutView();
            return View(form);
        }

        // Save the proof picture only now that everything else is valid
        string? proofName = null;
        if (form.PaymentMethod == PaymentMethod.Cashless && form.Proof is { Length: > 0 })
        {
            var (name, error) = await ProofStorage.SaveAsync(form.Proof, _env);
            if (error != null)
            {
                ModelState.AddModelError(nameof(form.Proof), error);
                await FillCheckoutView();
                return View(form);
            }
            proofName = name;
        }

        var order = new Order
        {
            StoreId = sid,
            UserId = CurrentUserId,
            CustomerName = form.CustomerName.Trim(),
            Phone = form.Phone.Trim(),
            OrderType = form.OrderType,
            Address = form.OrderType == OrderType.Delivery ? form.Address!.Trim() : null,
            DeliveryFee = form.OrderType == OrderType.Delivery ? store.DeliveryFee : 0,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.Now,
            ScheduledFor = hasPackage ? form.ScheduledFor : preferredAt,
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            PaymentMethod = form.PaymentMethod,
            PaymentProof = proofName,
            PaymentRef = form.PaymentMethod == PaymentMethod.Cashless && !string.IsNullOrWhiteSpace(form.PaymentRef) ? form.PaymentRef.Trim() : null,
            PaymentStatus = proofName != null ? PaymentStatus.ProofSent : PaymentStatus.Unpaid,
            DepositAmount = depositAmount
        };

        // Portions to take from today's menu (only dishes with a limit)
        var takes = new List<(int EntryId, int Qty, string Name)>();

        foreach (var line in cart)
        {
            var m = menu[line.MenuItemId];
            var opt = m.Options.FirstOrDefault(o => o.Id == line.OptionId);
            var tracksReadiness = !m.IsPackage;
            var entry = tracksReadiness ? todayEntries[m.Id] : null;
            order.Items.Add(new OrderItem
            {
                MenuItemId = m.Id,
                ItemName = opt != null ? m.Name + " - " + opt.Label : m.Name,
                UnitPrice = opt?.Price ?? m.Price,   // always the price from the database
                Quantity = line.Quantity,
                TracksReadiness = tracksReadiness,
                IsReadyToServe = entry?.ReadyState == DishReadyState.ReadyToServe,
                DailyMenuEntryId = entry?.Id
            });
            if (entry?.Stock != null) takes.Add((entry.Id, line.Quantity, m.Name));
        }

        // Remember the delivery address on the customer's profile for next time
        if (form.OrderType == OrderType.Delivery && form.SaveAddress)
        {
            var user = await _db.Users.FindAsync(CurrentUserId);
            if (user != null) user.Address = order.Address;
        }

        // Take the portions and save the order together. Each portion count is lowered in the
        // database only if enough are still left, so two customers ordering at the same moment
        // can never both get the last serving.
        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var take in takes)
        {
            var taken = await _db.DailyMenuEntries
                .Where(e => e.Id == take.EntryId && e.Stock != null && e.Stock >= take.Qty)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Stock, e => e.Stock - take.Qty));
            if (taken == 0)
            {
                await tx.RollbackAsync();
                ProofStorage.Delete(_env, proofName);
                ModelState.AddModelError("", "Sorry, " + take.Name + " just sold out or has fewer portions left. Please lower the quantity in your cart.");
                await FillCheckoutView();
                return View(form);
            }
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        CartHelper.Clear(HttpContext.Session);
        HttpContext.Session.SetInt32("lastOrderId", order.Id);

        return RedirectToAction(nameof(Status), new { id = order.Id });
    }

    public async Task<IActionResult> Status(int id)
    {
        var uid = CurrentUserId;
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();
        ViewBag.Store = await _db.StoreSettings.AsNoTracking().FirstAsync(s => s.Id == order.StoreId);
        return View(order);
    }

    // Send (or replace) the proof of payment after ordering
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProof(int id, IFormFile? proof, string? paymentRef)
    {
        var uid = CurrentUserId;
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();

        if (order.PaymentMethod != PaymentMethod.Cashless ||
            order.PaymentStatus == PaymentStatus.Paid || order.Status == OrderStatus.Cancelled)
            return RedirectToAction(nameof(Status), new { id });

        if (proof == null || proof.Length == 0)
        {
            TempData["Warn"] = "Please choose a picture of your payment.";
            return RedirectToAction(nameof(Status), new { id });
        }

        var (name, error) = await ProofStorage.SaveAsync(proof, _env);
        if (error != null)
        {
            TempData["Warn"] = error;
            return RedirectToAction(nameof(Status), new { id });
        }

        ProofStorage.Delete(_env, order.PaymentProof);
        order.PaymentProof = name;
        if (!string.IsNullOrWhiteSpace(paymentRef))
            order.PaymentRef = paymentRef.Trim().Length > 60 ? paymentRef.Trim()[..60] : paymentRef.Trim();
        order.PaymentStatus = PaymentStatus.ProofSent;
        await _db.SaveChangesAsync();

        TempData["Saved"] = "Proof sent. The carinderia will confirm your payment.";
        return RedirectToAction(nameof(Status), new { id });
    }

    // The orders that are still in progress (for the tracker bar)
    public async Task<IActionResult> Active()
    {
        var uid = CurrentUserId;
        var list = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId == uid && o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .ToListAsync();
        Response.Headers["Cache-Control"] = "no-store";
        return Json(new { orders = list.Select(OrderText.Snap) });
    }

    public async Task<IActionResult> StatusJson(int id)
    {
        var uid = CurrentUserId;
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();
        Response.Headers["Cache-Control"] = "no-store";
        return Json(new { status = order.Status.ToString() });
    }
}
