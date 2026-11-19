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

    public PaymentController(KaonDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // The carinderia's payment QR code. Any signed-in customer or owner may see it.
    public async Task<IActionResult> Qr()
    {
        var name = (await _db.StoreSettings.AsNoTracking().FirstAsync()).PaymentQr;
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
        if (!User.IsInRole("Owner") && order.UserId != uid) return NotFound();

        var path = ProofStorage.PathFor(_env, order.PaymentProof);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, ProofStorage.ContentType(order.PaymentProof));
    }
}
