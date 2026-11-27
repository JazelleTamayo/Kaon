using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

[Authorize(Roles = "Owner")]
public class OwnerController : Controller
{
    private readonly KaonDbContext _db;
    private readonly StoreContext _stores;
    public OwnerController(KaonDbContext db, StoreContext stores) { _db = db; _stores = stores; }

    private static bool IsActive(Order o) =>
        o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled;

    // Module 5: Owner order dashboard. "tab=packages" switches to the food-package pre-order board.
    public async Task<IActionResult> Index(string? tab)
    {
        var sid = await _stores.RequireAsync();
        var today = DateTime.Today;
        var all = await _db.Orders.Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync();
        var todays = all.Where(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1)).ToList();

        ViewBag.DishCount = await _db.MenuItems.CountAsync(m => m.StoreId == sid);
        ViewBag.Tab = tab;
        var active = all.Where(IsActive).OrderBy(o => o.CreatedAt).ToList();
        return View(new BoardVm
        {
            Active = active.Where(o => !o.IsPackage).ToList(),
            OrdersToday = todays.Count(o => o.Status != OrderStatus.Cancelled),
            SalesToday = todays.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total),
            Store = await _stores.StoreAsync(),
            Riders = (await _stores.StoreAsync()).UsesRiders
                ? await _db.Riders.AsNoTracking().Where(r => r.StoreId == sid && r.IsActive).OrderBy(r => r.Name).ToListAsync()
                : null,
            PackagePending = active.Where(o => o.IsPackage && o.Status == OrderStatus.Pending).ToList(),
            PackageReserved = active.Where(o => o.IsPackage && o.Status == OrderStatus.Confirmed)
                .OrderBy(o => o.ScheduledFor).ToList(),
            PackageKitchen = active.Where(o => o.IsPackage && o.Status is OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.OutForDelivery).ToList()
        });
    }

    // The dashboard checks this every few seconds and reloads when something changed.
    // Also reports which reserved packages just entered their "prepare soon" window, so the
    // owner gets nudged even if they're not staring at the Reserved list.
    public async Task<IActionResult> BoardState()
    {
        var sid = await _stores.RequireAsync();
        var rows = (await _db.Orders.Where(o => o.StoreId == sid)
                .Select(o => new { o.Id, o.Status, o.ScheduledFor, o.DepositAmount, Prices = o.Items.Select(i => i.UnitPrice) })
                .ToListAsync())
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .Select(o =>
            {
                var soon = o.DepositAmount != null && o.Status == OrderStatus.Confirmed && o.ScheduledFor != null &&
                    o.Prices.Any() &&
                    (o.ScheduledFor.Value.Date - DateTime.Today).TotalDays <= Math.Max(1, o.Prices.Max(PackageLeadTime.DaysFor) / 2);
                return o.Id + ":" + (int)o.Status + ":" + (soon ? 1 : 0);
            });
        return Json(new { sig = string.Join(",", rows) });
    }

    // Module 12: printable order slips for the kitchen, the counter, or the rider.
    // One order (Slip/5), or every order still on the board (Slips).
    [HttpGet]
    public async Task<IActionResult> Slip(int id)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        if (order == null) return NotFound();
        ViewBag.Store = await _stores.StoreAsync();
        ViewBag.Single = true;
        return View("Slip", new List<Order> { order });
    }

    [HttpGet]
    public async Task<IActionResult> Slips(OrderType? type, string? period, DateTime? from, DateTime? to)
    {
        var sid = await _stores.RequireAsync();
        var all = await _db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync();
        List<Order> orders;

        if (!string.IsNullOrEmpty(period))
        {
            // Every order of the period chosen on the Summary page
            var (start, end, chosen) = PeriodRange(period, from, to, null);
            orders = all.Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.Status != OrderStatus.Cancelled)
                .Where(o => type == null || o.OrderType == type)
                .OrderBy(o => o.CreatedAt).ToList();
            ViewBag.Period = chosen;
            ViewBag.From = start;
            ViewBag.To = end.AddDays(-1);
            ViewBag.RangeLabel = (end - start).TotalDays > 1
                ? start.ToString("MMM d") + " - " + end.AddDays(-1).ToString("MMM d, yyyy")
                : start.ToString("dddd, MMMM d, yyyy");
        }
        else
        {
            orders = all.Where(IsActive)
                .Where(o => type == null || o.OrderType == type)
                .OrderBy(o => o.CreatedAt).ToList();
        }

        ViewBag.Store = await _stores.StoreAsync();
        ViewBag.Type = type;
        return View("Slip", orders);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(int id)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        var next = order == null ? null : OrderText.Next(order);
        if (order != null && next != null)
        {
            order.Status = next.Value;
            if (next == OrderStatus.Completed) order.CompletedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Module 7: give a delivery order to a rider. Pick a saved rider, or type a new one (saved for next time).
    // The rider gets a private link with the address, the items and the amount to collect.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRider(int id, int? riderId, string? name, string? phone)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        if (order == null || order.OrderType != OrderType.Delivery || !IsActive(order)) return RedirectToAction(nameof(Index));

        Rider? rider = null;
        if (riderId != null)
        {
            rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == riderId && r.StoreId == sid && r.IsActive);
        }
        else if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(phone))
        {
            name = name.Trim(); phone = phone.Trim();
            if (name.Length > 60) name = name[..60];
            if (phone.Length > 20) phone = phone[..20];
            rider = await _db.Riders.FirstOrDefaultAsync(r => r.StoreId == sid && r.IsActive && r.Phone == phone);
            if (rider == null)
            {
                rider = new Rider { StoreId = sid, Name = name, Phone = phone };
                _db.Riders.Add(rider);
            }
        }
        if (rider == null)
        {
            TempData["Warn"] = "Choose a rider, or type the rider's name and number.";
            return RedirectToAction(nameof(Index));
        }

        // A different rider gets a new link, so the old rider's link stops working
        if (order.RiderToken == null || order.RiderPhone != rider.Phone)
            order.RiderToken = OrderText.NewRiderToken();
        order.RiderName = rider.Name;
        order.RiderPhone = rider.Phone;
        order.RiderAssignedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["Saved"] = "Order #" + order.Id + " is with " + rider.Name + ". Send them the rider link.";
        return RedirectToAction(nameof(Index));
    }

    // Take the rider off an order. Their link stops working right away.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRider(int id)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        if (order != null && IsActive(order))
        {
            order.RiderName = null;
            order.RiderPhone = null;
            order.RiderToken = null;
            order.RiderAssignedAt = null;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // The owner confirms money was received (cash collected, or a GCash proof checked).
    // Food packages pay in two steps: the deposit first, then the balance at pickup/delivery.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPaid(int id)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        if (order != null && order.Status != OrderStatus.Cancelled)
        {
            order.PaymentStatus = order.DepositAmount == null
                ? PaymentStatus.Paid
                : order.PaymentStatus switch
                {
                    PaymentStatus.DepositPaid or PaymentStatus.Paid => PaymentStatus.Paid,
                    _ => PaymentStatus.DepositPaid
                };
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, string? otherReason)
    {
        var sid = await _stores.RequireAsync();
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id && o.StoreId == sid);
        if (order != null && IsActive(order))
        {
            order.Status = OrderStatus.Cancelled;
            order.RiderToken = null;   // the rider's link stops working

            // The reason the customer will see: a quick choice, or the owner's own words for "Other"
            var why = (reason == "Other" ? otherReason : reason)?.Trim();
            if (string.IsNullOrEmpty(why) || why == "Other") why = null;
            order.CancelReason = why != null && why.Length > 200 ? why[..200] : why;

            // Give the portions back to today's menu
            var entryIds = order.Items.Where(i => i.DailyMenuEntryId != null).Select(i => i.DailyMenuEntryId!.Value).ToList();
            var entries = await _db.DailyMenuEntries.Where(e => entryIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
            foreach (var item in order.Items)
                if (item.DailyMenuEntryId != null && entries.TryGetValue(item.DailyMenuEntryId.Value, out var entry) && entry.Stock != null)
                    entry.Stock += item.Quantity;

            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleOpen()
    {
        var store = await _stores.StoreAsync(track: true);
        store.IsOpen = !store.IsOpen;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Module 8: Order history and end-of-day summary
    // The period chosen on the Summary page: start is the first day, end is the day AFTER the last day
    private static (DateTime Start, DateTime End, string Period) PeriodRange(string? period, DateTime? from, DateTime? to, DateTime? date)
    {
        var today = DateTime.Today;
        period = (period ?? "").ToLowerInvariant();

        if (date != null && period == "")            // an older link with one day
            return (date.Value.Date, date.Value.Date.AddDays(1), "custom");
        if (period == "yesterday") return (today.AddDays(-1), today, "yesterday");
        if (period == "week")
        {
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return (monday, monday.AddDays(7), "week");
        }
        if (period == "month")
        {
            var first = new DateTime(today.Year, today.Month, 1);
            return (first, first.AddMonths(1), "month");
        }
        if (period == "custom" && from != null)
        {
            var start = from.Value.Date;
            var last = (to ?? from).Value.Date;
            if (last < start) last = start;
            if ((last - start).TotalDays > 365) last = start.AddDays(365);
            return (start, last.AddDays(1), "custom");
        }
        return (today, today.AddDays(1), "today");
    }

    public async Task<IActionResult> Summary(string? period, DateTime? from, DateTime? to, DateTime? date)
    {
        var (start, end, chosen) = PeriodRange(period, from, to, date);
        var sid = await _stores.RequireAsync();
        var orders = (await _db.Orders.Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync())
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
            .OrderBy(o => o.CreatedAt).ToList();

        return View(new SummaryVm
        {
            Date = start,
            Start = start,
            End = end,
            Period = chosen,
            Orders = orders,
            Store = await _stores.StoreAsync()
        });
    }
}
