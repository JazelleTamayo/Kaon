using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 14: what customers say, the owner's log of orders that went wrong, and the owner's own survey
[Authorize(Roles = "Owner")]
public class OwnerFeedbackController : Controller
{
    private readonly KaonDbContext _db;
    private readonly StoreContext _stores;

    public OwnerFeedbackController(KaonDbContext db, StoreContext stores)
    {
        _db = db;
        _stores = stores;
    }

    public async Task<IActionResult> Index()
    {
        var sid = await _stores.RequireAsync();
        var since = DateTime.Now.AddDays(-30);
        return View(new OwnerFeedbackVm
        {
            Stats = await FeedbackNumbers.ForStoreAsync(_db, sid),
            Recent = await _db.OrderFeedbacks.AsNoTracking().Where(f => f.StoreId == sid)
                .OrderByDescending(f => f.CreatedAt).Take(30).ToListAsync(),
            Problems = await _db.OrderProblems.AsNoTracking().Where(p => p.StoreId == sid && p.At >= since)
                .OrderByDescending(p => p.At).ToListAsync(),
            LastSurvey = await _db.OwnerSurveys.AsNoTracking().Where(s => s.StoreId == sid)
                .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(),
            DeliveryFee = (await _stores.StoreAsync()).DeliveryFee
        });
    }

    // One tap: write down an order that went wrong
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LogProblem(ProblemKind kind, int? orderId, string? note)
    {
        var sid = await _stores.RequireAsync();
        if (!Enum.IsDefined(kind)) return RedirectToAction(nameof(Index));

        // Only this carinderia's own orders can be linked
        if (orderId != null && !await _db.Orders.AnyAsync(o => o.Id == orderId && o.StoreId == sid))
        {
            TempData["Warn"] = "There is no order #" + orderId + " at your carinderia. Leave the order number empty for walk-in orders.";
            return Redirect(Url.Action(nameof(Index))! + "#problems");
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 300) note = note[..300];
        _db.OrderProblems.Add(new OrderProblem { StoreId = sid, Kind = kind, OrderId = orderId, Note = note });
        await _db.SaveChangesAsync();
        TempData["Saved"] = "Written down: " + ProblemText.Label(kind).ToLower() + (orderId != null ? " (order #" + orderId + ")" : "") + ".";
        return Redirect(Url.Action(nameof(Index))! + "#problems");
    }

    // Undo a problem logged by mistake
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProblem(int id)
    {
        var sid = await _stores.RequireAsync();
        var p = await _db.OrderProblems.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == sid);
        if (p != null)
        {
            _db.OrderProblems.Remove(p);
            await _db.SaveChangesAsync();
            TempData["Saved"] = "Removed.";
        }
        return Redirect(Url.Action(nameof(Index))! + "#problems");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Survey(OwnerSurvey form)
    {
        var sid = await _stores.RequireAsync();
        if (!ModelState.IsValid)
        {
            TempData["Warn"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return Redirect(Url.Action(nameof(Index))! + "#survey");
        }

        _db.OwnerSurveys.Add(new OwnerSurvey
        {
            StoreId = sid,
            UserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            SeparationRating = form.SeparationRating,
            ProblemsPerWeekBefore = form.ProblemsPerWeekBefore,
            Comment = string.IsNullOrWhiteSpace(form.Comment) ? null : form.Comment.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Saved"] = "Thank you! Your answers were saved.";
        return Redirect(Url.Action(nameof(Index))! + "#survey");
    }
}
