using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Serves proof-of-payment pictures. Only the owner and the customer who placed the order can see one.
[Authorize]
public class PaymentController : Controller
{
    private readonly KaonDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly StoreContext _stores;

    public PaymentController(KaonDbContext db, IWebHostEnvironment env, StoreContext stores)
    {
        _db = db;
        _env = env;
        _stores = stores;
    }

    // A carinderia's payment QR code. Its own owner may see it, and customers who joined that carinderia.
    public async Task<IActionResult> Qr(int? storeId)
    {
        int? sid;
        if (User.IsInRole("Owner")) sid = await _stores.IdAsync();
        else if (User.IsInRole("Customer") && storeId != null &&
                 await _db.CustomerStores.AnyAsync(c => c.UserId == _stores.UserId && c.StoreId == storeId))
            sid = storeId;
        else return NotFound();
        if (sid == null) return NotFound();

        var name = (await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid))?.PaymentQr;
        if (string.IsNullOrEmpty(name)) return NotFound();

        var path = ProofStorage.PathFor(_env, name, "qr");
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, max-age=300";
        return PhysicalFile(path, ProofStorage.ContentType(name));
    }

    public async Task<IActionResult> Proof(int id)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order?.PaymentProof == null) return NotFound();

        var uid = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        // The owner of the carinderia the order was sent to, or the customer who placed it. Nobody else.
        if (User.IsInRole("Owner"))
        {
            if (order.StoreId != await _stores.IdAsync()) return NotFound();
        }
        else if (order.UserId != uid) return NotFound();

        var path = ProofStorage.PathFor(_env, order.PaymentProof);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, ProofStorage.ContentType(order.PaymentProof));
    }
}
