using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kaon.Helpers;

// Customers must be "at" a carinderia to see a menu, cart or orders. Owners must run one.
// A customer with none yet (they signed up without a carinderia's link) is sent to a page that
// explains how to get one. Nothing is listed there: carinderias are reached only by their own link or QR.
public class StoreFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> CustomerAreas = new() { "Menu", "Cart", "Order" };
    private static readonly HashSet<string> OwnerAreas = new() { "Owner", "OwnerMenu", "OwnerSettings" };
    private readonly StoreContext _store;

    public StoreFilter(StoreContext store) => _store = store;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";

        if (user.Identity?.IsAuthenticated == true)
        {
            if (user.IsInRole("Customer") && CustomerAreas.Contains(controller) && await _store.IdAsync() == null)
            {
                context.Result = new RedirectToActionResult("NoStore", "Store", null);
                return;
            }
            if (user.IsInRole("Owner") && OwnerAreas.Contains(controller) && await _store.IdAsync() == null)
            {
                context.Result = new ForbidResult();
                return;
            }
        }
        await next();
    }
}
