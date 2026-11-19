using Kaon.Data;
using Kaon.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

[Authorize(Roles = "Customer")]
public class CartController : Controller
{
    private readonly KaonDbContext _db;
    private readonly StoreContext _stores;
    public CartController(KaonDbContext db, StoreContext stores) { _db = db; _stores = stores; }

    public async Task<IActionResult> Index()
    {
        var store = await StoreGate.ForCustomersAsync(_db, await _stores.RequireAsync());
        ViewBag.Store = store;
        ViewBag.DeliveryFee = store.DeliveryFee;
        return View(CartHelper.Get(HttpContext.Session));
    }

    [HttpPost]
    public IActionResult Change(int id, int? optionId, int delta)
    {
        var cart = CartHelper.Get(HttpContext.Session);
        var line = cart.FirstOrDefault(c => c.MenuItemId == id && c.OptionId == optionId);
        if (line != null)
        {
            line.Quantity += delta;
            if (line.Quantity <= 0) cart.Remove(line);
        }
        CartHelper.Save(HttpContext.Session, cart);
        return RedirectToAction(nameof(Index));
    }
}
