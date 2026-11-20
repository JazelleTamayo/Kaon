using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Controllers;

// Module 11: Kaon admin back-office. Only the Kaon team can open these pages.
//
// Privacy rules built into this controller:
//  - The admin NEVER sees or sets anyone's password. They send a one-time link and the
//    person chooses their own password.
//  - The admin cannot see customers' personal details, orders or payment proofs.
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private const int LinkHours = 48;
    private readonly KaonDbContext _db;
    private readonly IEmailSender _email;

    public AdminController(KaonDbContext db, IEmailSender email)
    {
        _db = db;
        _email = email;
    }

    private int MyId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;

        var pending = await _db.PasswordTokens.AsNoTracking()
            .Where(t => t.UsedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Expires = g.Max(x => x.ExpiresAt) })
            .ToListAsync();

        var stores = await _db.StoreSettings.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        var stats = await StatsAsync(stores);
        return View(new AdminDashVm
        {
            Owners = await _db.Users.AsNoTracking().Where(u => u.Role == "Owner").OrderBy(u => u.FullName).ToListAsync(),
            NewRequests = await _db.CarinderiaRequests.CountAsync(r => r.Status == "New"),
            Stores = stores,
            Stats = stats,
            PayingCount = stats.Values.Count(x => x.Plan == "Paying"),
            TrialCount = stats.Values.Count(x => x.Plan == "Free trial"),
            EndedCount = stats.Values.Count(x => x.Plan == "Ended"),
            PendingLinks = pending.ToDictionary(p => p.UserId, p => p.Expires)
        });
    }

    // Owners and admins only. Customers' details are private and are not listed here.
    public async Task<IActionResult> Users(string? role)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.Role == "Owner" || u.Role == "Admin");
        if (role is "Owner" or "Admin") query = query.Where(u => u.Role == role);
        ViewBag.Role = role ?? "";
        ViewBag.MyId = MyId;
        ViewBag.StoreNames = await _db.StoreSettings.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
    }

    private async Task FillStores() =>
        ViewBag.Stores = await _db.StoreSettings.AsNoTracking().OrderBy(s => s.Name).ToListAsync();

    [HttpGet]
    public async Task<IActionResult> CreateOwner(int? requestId, int? storeId)
    {
        await FillStores();
        var form = new CreateOwnerForm { StoreId = storeId };
        // From a request: the details are filled in already
        if (requestId != null && await _db.CarinderiaRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.Status == "New") is { } r)
        {
            form.RequestId = r.Id; form.FullName = r.OwnerName; form.Email = r.Email;
            form.Phone = r.Phone; form.CarinderiaName = r.CarinderiaName;
        }
        return View(form);
    }

    // Carinderias that asked to join. The admin reviews each one and creates the account.
    public async Task<IActionResult> Requests(bool all = false)
    {
        var q = _db.CarinderiaRequests.AsNoTracking();
        if (!all) q = q.Where(r => r.Status == "New");
        ViewBag.All = all;
        // For approved requests: the owner account it became (to show its progress and open the carinderia)
        ViewBag.Owners = (await _db.Users.AsNoTracking().Where(u => u.Role == "Owner").ToListAsync())
            .GroupBy(u => u.Email).ToDictionary(g => g.Key, g => g.First());
        return View((await q.ToListAsync()).OrderByDescending(r => r.CreatedAt).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineRequest(int id)
    {
        var r = await _db.CarinderiaRequests.FirstOrDefaultAsync(x => x.Id == id && x.Status == "New");
        if (r != null) { r.Status = "Declined"; AdminLog.Add(_db, User, "Declined the request from " + r.CarinderiaName); await _db.SaveChangesAsync(); TempData["Saved"] = "Request from " + r.CarinderiaName + " declined."; }
        return RedirectToAction(nameof(Requests));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOwner(CreateOwnerForm form)
    {
        var email = form.Email.Trim().ToLower();
        if (await StaffEmailTaken(email))
            ModelState.AddModelError(nameof(form.Email), "That email already has an owner or Kaon team account.");

        StoreSettings? store = null;
        if (form.StoreId is int existingId and > 0)
        {
            store = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == existingId);
            if (store == null) ModelState.AddModelError(nameof(form.StoreId), "Choose a carinderia from the list.");
        }
        else if (string.IsNullOrWhiteSpace(form.CarinderiaName))
        {
            ModelState.AddModelError(nameof(form.CarinderiaName), "Enter the carinderia's name.");
        }
        if (!ModelState.IsValid)
        {
            await FillStores();
            return View(form);
        }

        var owner = await CreateOwnerAccount(form.FullName, email, form.Phone, store, form.CarinderiaName, form.RequestId);
        await InviteAndShow(owner, "Invitation ready");
        return RedirectToAction(nameof(Invite));
    }

    // Approve a request in one step: create the carinderia and its owner, then send the invitation
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRequest(int id)
    {
        var r = await _db.CarinderiaRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == "New");
        if (r == null) return RedirectToAction(nameof(Requests));

        if (await StaffEmailTaken(r.Email))
        {
            TempData["Warn"] = r.Email + " already has an owner or Kaon team account. Open \"Review first\" to use another email.";
            return RedirectToAction(nameof(Requests));
        }

        var owner = await CreateOwnerAccount(r.OwnerName, r.Email, r.Phone, null, r.CarinderiaName, r.Id);
        await InviteAndShow(owner, "Request approved");
        return RedirectToAction(nameof(Invite));
    }

    private Task<bool> StaffEmailTaken(string email)
    {
        email = email.Trim().ToLower();
        return _db.Users.AnyAsync(u => u.Email == email && u.Role != "Customer");
    }

    // A carinderia owner account with no password yet (and a new carinderia unless one is given)
    private async Task<AppUser> CreateOwnerAccount(string fullName, string email, string? phone, StoreSettings? store, string? newStoreName, int? requestId)
    {
        // A new carinderia is its own place: its own link, menu, orders and payment details. It starts empty.
        if (store == null)
        {
            var name = newStoreName!.Trim();
            store = new StoreSettings
            {
                Name = name,
                Slug = await StoreContext.UniqueSlugAsync(_db, name),
                Hours = null,
                GCashName = null,
                GCashNumber = null,
                DeliveryFee = 30,
                StarterPackagesAdded = true,   // no sample dishes: the owner adds their own
                DailyMenuSeeded = true
            };
            _db.StoreSettings.Add(store);
            await _db.SaveChangesAsync();
        }

        // The account starts with NO password. It cannot be used until the owner sets one.
        var owner = new AppUser
        {
            FullName = fullName.Trim(),
            Email = email.Trim().ToLower(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = "Owner",
            StoreId = store.Id,
            PasswordHash = ""
        };
        _db.Users.Add(owner);
        AdminLog.Add(_db, User, "Created the owner account for " + owner.FullName + " (" + owner.Email + ")", store);
        if (requestId != null && await _db.CarinderiaRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.Status == "New") is { } req)
        {
            req.Status = "Approved";
            AdminLog.Add(_db, User, "Approved the request from " + req.CarinderiaName, store);
        }
        await _db.SaveChangesAsync();

        // Module 9: every new carinderia starts with the free trial month
        _db.Subscriptions.Add(Billing.NewFor(owner.Id, await _db.PlatformSettings.AsNoTracking().FirstAsync(), DateTime.UtcNow));
        await _db.SaveChangesAsync();
        return owner;
    }

    // Make the one-time link, email it (when email is set up), and show it once to the admin
    private async Task InviteAndShow(AppUser owner, string heading)
    {
        var link = await IssueLink(owner, "Invite");
        TempData["InvMail"] = await _email.SendAsync(owner.Email, "Your Kaon account is ready",
            EmailTemplates.Invite(owner.FullName, link, LinkHours));
        ShowOnce(heading, owner.FullName, owner.Email, link, "Invite");
    }

    // A new one-time link so the person can choose a new password themselves.
    // Their current password keeps working until they use the link.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendLink(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || user.Id == MyId) return RedirectToAction(nameof(Index));

        var purpose = user.PasswordHash == "" ? "Invite" : "Reset";
        var link = await IssueLink(user, purpose);
        AdminLog.Add(_db, User, "Made a new " + (purpose == "Invite" ? "invitation" : "password reset") + " link for " + user.FullName, user.StoreId is int sid0 ? await _db.StoreSettings.FindAsync(sid0) : null);
        await _db.SaveChangesAsync();
        TempData["InvMail"] = await _email.SendAsync(user.Email,
            purpose == "Invite" ? "Your Kaon account is ready" : "Reset your Kaon password",
            purpose == "Invite" ? EmailTemplates.Invite(user.FullName, link, LinkHours) : EmailTemplates.Reset(user.FullName, link, LinkHours * 60));
        ShowOnce(purpose == "Invite" ? "New invitation link" : "Password reset link", user.FullName, user.Email, link, purpose);
        return RedirectToAction(nameof(Invite));
    }

    // Turn an account off or back on. It is signed out on its next request.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, string? back)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null && user.Id != MyId && user.Role is "Owner" or "Admin")
        {
            user.IsActive = !user.IsActive;
            AdminLog.Add(_db, User, (user.IsActive ? "Switched on " : "Switched off ") + user.FullName + "'s account", user.StoreId is int sid1 ? await _db.StoreSettings.FindAsync(sid1) : null);
            await _db.SaveChangesAsync();
            TempData["Saved"] = user.FullName + (user.IsActive ? " is switched on." : " is switched off. They can't log in.");
        }
        return back == "team" ? RedirectToAction(nameof(Team)) : back == "users" ? RedirectToAction(nameof(Users)) : RedirectToAction(nameof(Index));
    }

    // Shown once, right after creating. A refresh does not show it again.
    public IActionResult Invite()
    {
        var title = TempData["InvTitle"] as string;
        var link = TempData["InvLink"] as string;
        if (title == null || link == null) return RedirectToAction(nameof(Index));

        ViewBag.Heading = title;
        ViewBag.Name = TempData["InvName"] as string;
        ViewBag.Email = TempData["InvEmail"] as string;
        ViewBag.Link = link;
        ViewBag.Purpose = TempData["InvPurpose"] as string;
        ViewBag.Mail = TempData["InvMail"] as string;
        ViewBag.Hours = LinkHours;
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }


    // ---------- the carinderias: menu size and plan only (never customers or orders) ----------
    private async Task<Dictionary<int, StoreStats>> StatsAsync(List<StoreSettings> stores)
    {
        var ids = stores.Select(x => x.Id).ToList();
        var dishes = await _db.MenuItems.AsNoTracking().Where(m => ids.Contains(m.StoreId))
            .GroupBy(m => m.StoreId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);

        var owners = await _db.Users.AsNoTracking().Where(u => u.Role == "Owner" && u.StoreId != null && ids.Contains(u.StoreId.Value)).ToListAsync();
        var subs = await _db.Subscriptions.AsNoTracking().ToListAsync();
        var now = DateTime.UtcNow;

        var result = new Dictionary<int, StoreStats>();
        foreach (var st in stores)
        {
            var stat = new StoreStats
            {
                Dishes = dishes.GetValueOrDefault(st.Id)
            };

            // The plan of the owner with the longest access
            var best = owners.Where(o => o.StoreId == st.Id)
                .Select(o => subs.FirstOrDefault(x => x.OwnerId == o.Id)).Where(x => x != null)
                .OrderByDescending(x => x!.AccessUntil).FirstOrDefault();
            if (best != null)
            {
                var status = best.StatusAt(now);
                stat.Plan = status == SubscriptionStatus.Trial ? "Free trial" : status == SubscriptionStatus.Active ? "Paying" : "Ended";
                stat.PlanClass = status == SubscriptionStatus.Trial ? "chip-pending" : status == SubscriptionStatus.Active ? "chip-ready" : "chip-cancelled";
                stat.AccessUntil = best.AccessUntil;
                stat.DaysLeft = best.DaysLeft(now);
            }
            if (st.SuspendedByAdmin) { stat.Plan = "Paused by Kaon"; stat.PlanClass = "chip-cancelled"; }
            result[st.Id] = stat;
        }
        return result;
    }

    // ---------- one carinderia: the support view ----------
    [HttpGet("admin/carinderia/{id:int}")]
    public async Task<IActionResult> Store(int id)
    {
        var store = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();

        // Opening a carinderia's support view is written in the activity log (not more than once in 10 minutes)
        var tenMin = DateTime.UtcNow.AddMinutes(-10);
        if (!await _db.ActivityLogs.AnyAsync(a => a.AdminId == MyId && a.StoreId == id && a.At > tenMin && a.Action.StartsWith("Opened")))
        {
            AdminLog.Add(_db, User, "Opened the support view", store);
            await _db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var pending = await _db.PasswordTokens.AsNoTracking()
            .Where(t => t.UsedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId).Select(g => new { UserId = g.Key, Expires = g.Max(x => x.ExpiresAt) }).ToListAsync();

        var vm = new StoreDetailVm
        {
            Store = store,
            Link = Url.Action("Landing", "Store", new { slug = store.Slug }, Request.Scheme)!,
            Owners = await _db.Users.AsNoTracking().Where(u => u.Role == "Owner" && u.StoreId == id).OrderBy(u => u.FullName).ToListAsync(),
            PendingLinks = pending.ToDictionary(x => x.UserId, x => x.Expires),
            Stats = (await StatsAsync(new List<StoreSettings> { store }))[id],
            Activity = await _db.ActivityLogs.AsNoTracking().Where(a => a.StoreId == id).OrderByDescending(a => a.At).Take(10).ToListAsync()
        };
        ViewBag.Series = MenuSeries.All;
        return View(vm);
    }

    [HttpPost("admin/carinderia/{id:int}/rename"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameStore(int id, RenameStoreForm form)
    {
        var store = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Warn"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Store), new { id });
        }

        var oldName = store.Name; var oldSlug = store.Slug;
        store.Name = form.Name.Trim();
        store.Slug = await StoreContext.UniqueSlugAsync(_db, form.Slug, id);
        if (oldName != store.Name || oldSlug != store.Slug)
            AdminLog.Add(_db, User, "Changed the name or web address (" + oldName + " /c/" + oldSlug + " to " + store.Name + " /c/" + store.Slug + ")", store);
        await _db.SaveChangesAsync();
        TempData["Saved"] = oldSlug != store.Slug ? "Saved. The web address changed, so any printed QR code must be printed again." : "Saved.";
        return RedirectToAction(nameof(Store), new { id });
    }

    [HttpPost("admin/carinderia/{id:int}/suspend"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSuspend(int id)
    {
        var store = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();
        store.SuspendedByAdmin = !store.SuspendedByAdmin;
        AdminLog.Add(_db, User, store.SuspendedByAdmin ? "Paused the carinderia (customers cannot order)" : "Resumed the carinderia", store);
        await _db.SaveChangesAsync();
        TempData["Saved"] = store.SuspendedByAdmin ? store.Name + " is paused. Customers see it as not taking orders." : store.Name + " is back.";
        return RedirectToAction(nameof(Store), new { id });
    }

    // Day-one menu for an owner who has no dishes yet
    [HttpPost("admin/carinderia/{id:int}/starter-menu"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStarterMenu(int id)
    {
        var store = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();
        var added = DemoData.AddStarterMenu(_db, id);
        if (added == 0) { TempData["Warn"] = "This carinderia already has dishes, so nothing was added."; return RedirectToAction(nameof(Store), new { id }); }
        AdminLog.Add(_db, User, "Added the starter menu (" + added + " dishes)", store);
        await _db.SaveChangesAsync();
        TempData["Saved"] = added + " sample dishes were added and today's menu is ready. The owner can change the names, prices and photos.";
        return RedirectToAction(nameof(Store), new { id });
    }

    // Add one dish for an owner and put it on today's menu
    [HttpPost("admin/carinderia/{id:int}/dish"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDish(int id, AddDishForm form)
    {
        var store = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (store == null) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Warn"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Store), new { id });
        }

        var dish = new MenuItem
        {
            StoreId = id,
            Name = form.Name.Trim(),
            Price = form.Price,
            Category = "Meal",
            Series = !string.IsNullOrEmpty(form.Series) && MenuSeries.All.Contains(form.Series) ? form.Series : MenuSeries.Other,
            IsAvailable = true
        };
        _db.MenuItems.Add(dish);
        await _db.SaveChangesAsync();
        _db.DailyMenuEntries.Add(new DailyMenuEntry { MenuItemId = dish.Id, Date = DateTime.Today });
        AdminLog.Add(_db, User, "Added the dish " + dish.Name + " (₱" + dish.Price.ToString("N0") + ")", store);
        await _db.SaveChangesAsync();
        TempData["Saved"] = dish.Name + " was added to " + store.Name + " and to today's menu.";
        return RedirectToAction(nameof(Store), new { id });
    }

    // ---------- the Kaon team ----------
    [HttpGet("admin/team")]
    public async Task<IActionResult> Team()
    {
        var now = DateTime.UtcNow;
        var pending = await _db.PasswordTokens.AsNoTracking()
            .Where(t => t.UsedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId).Select(g => new { UserId = g.Key, Expires = g.Max(x => x.ExpiresAt) }).ToListAsync();
        ViewBag.PendingLinks = pending.ToDictionary(x => x.UserId, x => x.Expires);
        ViewBag.MyId = MyId;
        return View(await _db.Users.AsNoTracking().Where(u => u.Role == "Admin").OrderBy(u => u.FullName).ToListAsync());
    }

    [HttpGet("admin/team/add")]
    public IActionResult AddAdmin() => View(new AddAdminForm());

    [HttpPost("admin/team/add"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAdmin(AddAdminForm form)
    {
        var email = form.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email && u.Role != "Customer"))
            ModelState.AddModelError(nameof(form.Email), "That email already has a Kaon team or owner account.");
        if (!ModelState.IsValid) return View(form);

        // Like owners: no password until the person chooses their own from the private link
        var admin = new AppUser { FullName = form.FullName.Trim(), Email = email, Role = "Admin", PasswordHash = "" };
        _db.Users.Add(admin);
        AdminLog.Add(_db, User, "Added " + admin.FullName + " (" + admin.Email + ") to the Kaon team");
        await _db.SaveChangesAsync();

        var link = await IssueLink(admin, "Invite");
        TempData["InvMail"] = await _email.SendAsync(admin.Email, "Your Kaon admin account is ready",
            EmailTemplates.Invite(admin.FullName, link, LinkHours));
        ShowOnce("Invitation ready", admin.FullName, admin.Email, link, "Invite");
        return RedirectToAction(nameof(Invite));
    }

    // ---------- what the Kaon team did ----------
    [HttpGet("admin/activity")]
    public async Task<IActionResult> Activity(int? storeId)
    {
        var q = _db.ActivityLogs.AsNoTracking().AsQueryable();
        if (storeId != null) q = q.Where(a => a.StoreId == storeId);
        ViewBag.StoreId = storeId;
        ViewBag.Stores = await _db.StoreSettings.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        return View(await q.OrderByDescending(a => a.At).Take(200).ToListAsync());
    }

    // A fresh link replaces any older one for that person
    private async Task<string> IssueLink(AppUser user, string purpose)
    {
        var old = await _db.PasswordTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync();
        foreach (var o in old) o.UsedAt = DateTime.UtcNow;

        var (token, hash) = InviteTokens.New();
        _db.PasswordTokens.Add(new PasswordToken
        {
            UserId = user.Id,
            TokenHash = hash,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddHours(LinkHours)
        });
        await _db.SaveChangesAsync();

        return Url.Action("SetPassword", "Account", new { token }, Request.Scheme)!;
    }

    private void ShowOnce(string title, string name, string email, string link, string purpose)
    {
        TempData["InvTitle"] = title;
        TempData["InvName"] = name;
        TempData["InvEmail"] = email;
        TempData["InvLink"] = link;
        TempData["InvPurpose"] = purpose;
    }
}
