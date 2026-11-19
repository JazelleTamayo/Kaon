using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kaon.Helpers;

// A person who just received a temporary password must choose their own before doing anything else.
public class MustChangePasswordFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true && user.HasClaim("must_change", "1"))
        {
            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();
            var allowed = controller == "Account" &&
                          (action == "ChangePassword" || action == "Logout" || action == "AccessDenied");
            if (!allowed)
            {
                context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
                return;
            }
        }
        await next();
    }
}
