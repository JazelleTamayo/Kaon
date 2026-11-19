using System.Security.Claims;
using Kaon.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// Module 9: an owner whose free month or paid period has ended can still log in and pay,
// but can't use the order board, menu or store pages until the subscription is renewed.
public class SubscriptionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> OwnerAreas = new() { "Owner", "OwnerMenu", "OwnerSettings" };
    private readonly KaonDbContext _db;

    public SubscriptionFilter(KaonDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";

        if (user.Identity?.IsAuthenticated == true && user.IsInRole("Owner") && OwnerAreas.Contains(controller) &&
            int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
        {
            var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == ownerId);
            if (sub != null && !sub.HasAccess(DateTime.UtcNow))
            {
                if (context.HttpContext.Request.Headers.XRequestedWith == "XMLHttpRequest")
                {
                    context.Result = new StatusCodeResult(StatusCodes.Status402PaymentRequired);
                    return;
                }
                if (context.Controller is Controller c)
                    c.TempData["Warn"] = "Your Kaon subscription has ended. Renew below to open your orders, menu and store again.";
                context.Result = new RedirectToActionResult("Index", "Billing", null);
                return;
            }
        }
        await next();
    }
}
