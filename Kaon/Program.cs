using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<MustChangePasswordFilter>();
    options.Filters.Add<StoreFilter>();
    options.Filters.Add<SubscriptionFilter>();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<StoreContext>();

builder.Services.AddDbContext<KaonDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("KaonDb")));

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<IEmailSender, EmailSender>();

builder.Services.AddAuthentication("KaonCookie")
    .AddCookie("KaonCookie", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;                              // scripts cannot read the sign-in cookie
        options.Cookie.SameSite = SameSiteMode.Lax;                  // not sent along with other sites' requests
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // https-only when the site runs on https

        // Check on every page request that the account still exists and is switched on,
        // so turning an account off takes effect straight away.
        options.Events.OnValidatePrincipal = async context =>
        {
            var path = context.HttpContext.Request.Path;
            if (path.StartsWithSegments("/css") || path.StartsWithSegments("/js") ||
                path.StartsWithSegments("/img") || path.StartsWithSegments("/lib")) return;

            var ok = false;
            if (int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<KaonDbContext>();
                var u = await db.Users.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new { x.IsActive, x.Role })
                    .FirstOrDefaultAsync();
                ok = u != null && u.IsActive && u.Role == context.Principal!.FindFirstValue(ClaimTypes.Role);
            }
            if (!ok)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync("KaonCookie");
            }
        };
    });

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KaonDbContext>();
    db.Database.Migrate();

    if (!db.StoreSettings.Any())
    {
        db.StoreSettings.Add(new StoreSettings
        {
            DeliveryFee = builder.Configuration.GetValue<decimal?>("Kaon:DeliveryFee") ?? 30
        });
        db.SaveChanges();
    }

    // Module 9: how Kaon charges carinderias. The price is a placeholder until the team confirms it.
    if (!db.PlatformSettings.Any())
    {
        db.PlatformSettings.Add(new PlatformSettings
        {
            MonthlyFee = builder.Configuration.GetValue<decimal?>("Kaon:Subscription:MonthlyFee") ?? 299,
            TrialDays = builder.Configuration.GetValue<int?>("Kaon:Subscription:TrialDays") ?? 30,
            GCashNumber = builder.Configuration["Kaon:Subscription:GCashNumber"]
        });
        db.SaveChanges();
    }

    DemoData.Seed(db, builder.Configuration["Kaon:Demo:GCashNumber"]);

    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

    // Kaon admin account. The email and password come from settings (user secrets or
    // environment variables: Kaon__Admin__Email, Kaon__Admin__Password), never from the code.
    // Only when developing on your own computer is there a default, so you can log in.
    var adminEmail = builder.Configuration["Kaon:Admin:Email"];
    var adminPassword = builder.Configuration["Kaon:Admin:Password"];
    if (app.Environment.IsDevelopment())
    {
        adminEmail ??= "admin@kaon.local";
        adminPassword ??= "Admin123!";
    }

    if (!db.Users.Any(u => u.Role == "Admin"))
    {
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = new AppUser { FullName = "Kaon Admin", Email = adminEmail.Trim().ToLower(), Role = "Admin" };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            db.SaveChanges();
        }
        else
        {
            app.Logger.LogWarning("No Kaon admin account exists. Set Kaon:Admin:Email and Kaon:Admin:Password to create one.");
        }
    }

    // A demo owner exists ONLY when developing locally. In production the admin creates owners.
    if (app.Environment.IsDevelopment() && !db.Users.Any(u => u.Role == "Owner"))
    {
        var owner = new AppUser { FullName = "Kaon Owner", Email = "owner@kaon.local", Role = "Owner", StoreId = db.StoreSettings.OrderBy(x => x.Id).First().Id };
        owner.PasswordHash = hasher.HashPassword(owner, "Owner123!");
        db.Users.Add(owner);
        db.SaveChanges();
    }

    // Every owner has a subscription. Owners who existed before billing get their free month starting now.
    var platform = db.PlatformSettings.First();
    var ownerIds = db.Users.Where(u => u.Role == "Owner").Select(u => u.Id).ToList();
    var subscribed = db.Subscriptions.Select(x => x.OwnerId).ToHashSet();
    foreach (var id in ownerIds.Where(id => !subscribed.Contains(id)))
        db.Subscriptions.Add(Billing.NewFor(id, platform, DateTime.UtcNow));
    db.SaveChanges();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Standard security headers on every response
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";                          // do not guess file types
    h["X-Frame-Options"] = "DENY";                                    // no one can show Kaon inside another site
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// The home page is the public landing page
app.MapControllerRoute(
    name: "root",
    pattern: "",
    defaults: new { controller = "Home", action = "Index" })
    .WithStaticAssets();

// /Owner, /Menu, /Order and so on open their Index page
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();