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
    public OwnerController(KaonDbContext db) => _db = db;

    private static bool IsActive(Order o) =>
        o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled;

    // Module 5: Owner order dashboard
    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        var all = await _db.Orders.Include(o => o.Items).ToListAsync();
        var todays = all.Where(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1)).ToList();

        return View(new BoardVm
        {
            Active = all.Where(IsActive).OrderBy(o => o.CreatedAt).ToList(),
            OrdersToday = todays.Count(o => o.Status != OrderStatus.Cancelled),
            SalesToday = todays.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total),
            Store = await _db.StoreSettings.AsNoTracking().FirstAsync()
        });
    }

    // The dashboard checks this every few seconds and reloads when something changed
    public async Task<IActionResult> BoardState()
    {
        var rows = (await _db.Orders.Select(o => new { o.Id, o.Status }).ToListAsync())
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .Select(o => o.Id + ":" + (int)o.Status);
        return Json(new { sig = string.Join(",", rows) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        var next = order == null ? null : OrderText.Next(order);
        if (order != null && next != null)
        {
            order.Status = next.Value;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order != null && IsActive(order))
        {
            order.Status = OrderStatus.Cancelled;

            // Give the portions back to the menu
            var ids = order.Items.Select(i => i.MenuItemId).ToList();
            var menu = await _db.MenuItems.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            foreach (var item in order.Items)
                if (menu.TryGetValue(item.MenuItemId, out var m) && m.Stock != null)
                    m.Stock += item.Quantity;

            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleOpen()
    {
        var store = await _db.StoreSettings.FirstAsync();
        store.IsOpen = !store.IsOpen;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Module 8: Order history and end-of-day summary
    public async Task<IActionResult> Summary(DateTime? date)
    {
        var day = (date ?? DateTime.Today).Date;
        var orders = (await _db.Orders.Include(o => o.Items).ToListAsync())
            .Where(o => o.CreatedAt >= day && o.CreatedAt < day.AddDays(1))
            .OrderBy(o => o.CreatedAt).ToList();

        return View(new SummaryVm
        {
            Date = day,
            Orders = orders,
            Store = await _db.StoreSettings.AsNoTracking().FirstAsync()
        });
    }
}
