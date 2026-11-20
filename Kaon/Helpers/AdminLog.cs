using System.Security.Claims;
using Kaon.Data;
using Kaon.Models;

namespace Kaon.Helpers;

// Writes one line in the Kaon team's activity log. The caller saves it with its own SaveChanges.
public static class AdminLog
{
    public static void Add(KaonDbContext db, ClaimsPrincipal admin, string action, StoreSettings? store = null)
    {
        int.TryParse(admin.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
        db.ActivityLogs.Add(new ActivityLog
        {
            AdminId = id,
            AdminName = admin.FindFirstValue(ClaimTypes.Name) ?? "Admin",
            Action = action.Length > 250 ? action[..250] : action,
            StoreId = store?.Id,
            StoreName = store?.Name
        });
    }
}
