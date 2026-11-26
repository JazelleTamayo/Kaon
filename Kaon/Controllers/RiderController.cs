using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 7: the rider's private page for ONE delivery. No account or login: the link itself is the key.
//  - The link stops working when the owner removes or changes the rider, or cancels the order.
//  - Once the order is delivered, the page no longer shows the customer's details.
[AllowAnonymous]
public class RiderController : Controller
{
    private readonly KaonDbContext _db;
    public RiderController(KaonDbContext db) => _db = db;

    private async Task<Order?> FindAsync(string token, bool track = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
        var q = track ? _db.Orders : _db.Orders.AsNoTracking();
        return await q.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.RiderToken == token && o.OrderType == OrderType.Delivery && o.Status != OrderStatus.Cancelled);
    }

    private void PrivatePage()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    [HttpGet("/r/{token}")]
    public async Task<IActionResult> Index(string token)
    {
        PrivatePage();
        var order = await FindAsync(token);
        if (order == null) return View("Gone");

        ViewBag.Store = await _db.StoreSettings.AsNoTracking().FirstAsync(s => s.Id == order.StoreId);
        ViewBag.Token = token;
        return View(order);
    }

    // The rider has the food and is on the way
    [HttpPost("/r/{token}/picked-up"), ValidateAntiForgeryToken]
    public async Task<IActionResult> PickedUp(string token)
    {
        var order = await FindAsync(token, track: true);
        if (order == null) return RedirectToAction(nameof(Index), new { token });

        if (order.Status is OrderStatus.Pending or OrderStatus.Preparing or OrderStatus.Ready)
        {
            order.Status = OrderStatus.OutForDelivery;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { token });
    }

    // Handed to the customer. If cash was due, the rider confirms they collected it.
    [HttpPost("/r/{token}/delivered"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delivered(string token, bool collected)
    {
        var order = await FindAsync(token, track: true);
        if (order == null) return RedirectToAction(nameof(Index), new { token });

        if (order.Status == OrderStatus.OutForDelivery)
        {
            // Cash is due: the rider must confirm it. A GCash proof waiting for the owner's check is not asked for again.
            if (OrderText.ToCollect(order) > 0 && order.PaymentStatus != PaymentStatus.ProofSent && !collected)
            {
                TempData["Warn"] = "Tick the box once you have collected the payment.";
                return RedirectToAction(nameof(Index), new { token });
            }
            if (collected) order.PaymentStatus = PaymentStatus.Paid;
            order.Status = OrderStatus.Completed;
            order.CompletedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { token });
    }
}
