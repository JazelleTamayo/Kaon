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

    public OrderController(KaonDbContext db) => _db = db;

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
        return View(orders);
    }

    private async Task FillCheckoutView()
    {
        var store = await _db.StoreSettings.AsNoTracking().FirstAsync();
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
            OrderType = store.PickupEnabled ? OrderType.Pickup : OrderType.Delivery
        });
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutForm form)
    {
        var cart = CartHelper.Get(HttpContext.Session);
        if (!cart.Any()) return RedirectToAction("Index", "Menu");

        var store = await _db.StoreSettings.AsNoTracking().FirstAsync();

        if (!store.IsOpen)
            ModelState.AddModelError("", "Sorry, " + store.Name + " is closed right now. Please try again later.");
        if (form.OrderType == OrderType.Pickup && !store.PickupEnabled)
            ModelState.AddModelError("", "Pickup is not available right now.");
        if (form.OrderType == OrderType.Delivery && !store.DeliveryEnabled)
            ModelState.AddModelError("", "Home delivery is not available right now.");
        if (form.OrderType == OrderType.Delivery && string.IsNullOrWhiteSpace(form.Address))
            ModelState.AddModelError(nameof(form.Address), "Please enter your delivery address.");

        var ids = cart.Select(c => c.MenuItemId).ToList();
        var menu = await _db.MenuItems
            .Include(m => m.Options)
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var unavailable = cart
            .Where(c => !menu.ContainsKey(c.MenuItemId) || !menu[c.MenuItemId].CanOrder)
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
            if (m.Stock != null && line.Quantity > m.Stock)
                ModelState.AddModelError("", "Only " + m.Stock + " " + m.Name + " left. Please lower the quantity in your cart.");
            if (m.Options.Any() && m.Options.All(o => o.Id != line.OptionId))
                ModelState.AddModelError("", "Please remove " + m.Name + " from your cart and add it again with a size.");
        }

        // Food packages are made ahead: the customer must say when they need them
        var hasPackage = cart.Any(c => menu.TryGetValue(c.MenuItemId, out var mi) && mi.IsPackage);
        if (hasPackage)
        {
            if (form.ScheduledFor == null)
                ModelState.AddModelError(nameof(form.ScheduledFor), "Please tell us when you need your food package.");
            else if (form.ScheduledFor < DateTime.Now.AddHours(24))
                ModelState.AddModelError(nameof(form.ScheduledFor), "Food packages need at least 1 day notice.");
        }

        if (!ModelState.IsValid)
        {
            await FillCheckoutView();
            return View(form);
        }

        var order = new Order
        {
            UserId = CurrentUserId,
            CustomerName = form.CustomerName.Trim(),
            Phone = form.Phone.Trim(),
            OrderType = form.OrderType,
            Address = form.OrderType == OrderType.Delivery ? form.Address!.Trim() : null,
            DeliveryFee = form.OrderType == OrderType.Delivery ? store.DeliveryFee : 0,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.Now,
            ScheduledFor = hasPackage ? form.ScheduledFor : null
        };

        foreach (var line in cart)
        {
            var m = menu[line.MenuItemId];
            var opt = m.Options.FirstOrDefault(o => o.Id == line.OptionId);
            order.Items.Add(new OrderItem
            {
                MenuItemId = m.Id,
                ItemName = opt != null ? m.Name + " - " + opt.Label : m.Name,
                UnitPrice = opt?.Price ?? m.Price,   // always the price from the database
                Quantity = line.Quantity
            });
            if (m.Stock != null) m.Stock -= line.Quantity;
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

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
        return View(order);
    }

    public async Task<IActionResult> StatusJson(int id)
    {
        var uid = CurrentUserId;
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();
        return Json(new { status = order.Status.ToString() });
    }
}
