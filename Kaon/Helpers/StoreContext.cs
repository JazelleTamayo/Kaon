using System.Security.Claims;
using System.Text;
using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Helpers;

// Which carinderia is this request about?
//  - An owner: the carinderia they run (AppUser.StoreId). Never anything else.
//  - A customer: the carinderia they signed up at (AppUser.StoreId). Each carinderia has its own customers.
// Every menu, order and payment query is filtered by this id, so one carinderia never sees another's data.
public class StoreContext
{
    private const string SessionKey = "storeId";
    private readonly KaonDbContext _db;
    private readonly IHttpContextAccessor _http;
    private bool _loaded;
    private int? _id;

    public StoreContext(KaonDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    private HttpContext Http => _http.HttpContext!;
    private ClaimsPrincipal User => Http.User;

    public int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<int?> IdAsync()
    {
        if (_loaded) return _id;
        _loaded = true;

        if (User.Identity?.IsAuthenticated != true || UserId is not int uid) return _id = null;

        // An owner runs one carinderia; a customer belongs to the carinderia they signed up at
        if (User.IsInRole("Owner") || User.IsInRole("Customer"))
            _id = await _db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.StoreId).FirstOrDefaultAsync();
        return _id;
    }

    // For code that runs after the filter has made sure a carinderia exists
    public async Task<int> RequireAsync() => await IdAsync() ?? throw new InvalidOperationException("No carinderia for this request.");

    // The store settings row
    public async Task<StoreSettings> StoreAsync(bool track = false)
    {
        var id = await RequireAsync();
        var q = track ? _db.StoreSettings : _db.StoreSettings.AsNoTracking();
        return await q.FirstAsync(s => s.Id == id);
    }

    // A visitor opened this carinderia's link or QR: the next sign-in or sign-up is for it
    public void Remember(int storeId)
    {
        var s = Http.Session;
        if (s.GetInt32(SessionKey) != storeId) CartHelper.Clear(s);
        s.SetInt32(SessionKey, storeId);
    }

    // The web address text: "Jamaica Foods" -> "jamaica-foods"
    public static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant().Replace("'", "").Replace("’", ""))
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        if (s.Length > 50) s = s[..50].Trim('-');
        return s.Length == 0 ? "carinderia" : s;
    }

    // A slug no other carinderia uses
    public static async Task<string> UniqueSlugAsync(KaonDbContext db, string wanted, int? exceptStoreId = null)
    {
        var baseSlug = Slugify(wanted);
        var slug = baseSlug;
        for (var n = 2; await db.StoreSettings.AnyAsync(s => s.Slug == slug && s.Id != exceptStoreId); n++)
            slug = baseSlug + "-" + n;
        return slug;
    }
}
