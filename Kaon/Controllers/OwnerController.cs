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

    // Module 5: Owner order dashboard
    public async Task<IActionResult> Index()
    {
        var sid = await _stores.RequireAsync();
        var today = DateTime.Today;
        var all = await _db.Orders.Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync();
        var todays = all.Where(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1)).ToList();

        ViewBag.DishCount = await _db.MenuItems.CountAsync(m => m.StoreId == sid);
        return View(new BoardVm
        {
            Active = all.Where(IsActive).OrderBy(o => o.CreatedAt).ToList(),
            OrdersToday = todays.Count(o => o.Status != OrderStatus.Cancelled),
            SalesToday = todays.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total),
            Store = await _stores.StoreAsync()
        });
    }

    // The dashboard checks this every few seconds and reloads when something changed
    public async Task<IActionResult> BoardState()
    {
        var sid = await _stores.RequireAsync();
        var rows = (await _db.Orders.Where(o => o.StoreId == sid).Select(o => new { o.Id, o.Status }).ToListAsync())
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .Select(o => o.Id + ":" + (int)o.Status);
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
    public async Task<IActionResult> Slips(OrderType? type)
    {
        var sid = await _stores.RequireAsync();
        var orders = (await _db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync())
            .Where(IsActive)
            .Where(o => type == null || o.OrderType == type)
            .OrderBy(o => o.CreatedAt).ToList();
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
    public async Task<IActionResult> Summary(DateTime? date)
    {
        var day = (date ?? DateTime.Today).Date;
        var sid = await _stores.RequireAsync();
        var orders = (await _db.Orders.Include(o => o.Items).Where(o => o.StoreId == sid).ToListAsync())
            .Where(o => o.CreatedAt >= day && o.CreatedAt < day.AddDays(1))
            .OrderBy(o => o.CreatedAt).ToList();

        return View(new SummaryVm
        {
            Date = day,
            Orders = orders,
            Store = await _stores.StoreAsync()
        });
    }
}
