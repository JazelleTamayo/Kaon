using System.Security.Claims;
using Kaon.Data;
using Kaon.Helpers;
using Kaon.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    // Every form post must carry the hidden security token the page put in it (stops other
    // websites from submitting forms, such as log out or place order, in a user's name)
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<MustChangePasswordFilter>();
    options.Filters.Add<StoreFilter>();
    options.Filters.Add<SubscriptionFilter>();
});

// On a real server, keep the key that protects the login cookie in a file. Without it, every restart or
// recycle of the app makes a new key and signs everybody out.
if (!builder.Environment.IsDevelopment())
{
    var keyRoot = builder.Configuration["Kaon:DataDir"];
    if (string.IsNullOrWhiteSpace(keyRoot)) keyRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(keyRoot, "keys")))
        .SetApplicationName("Kaon");
}

builder.Services.AddHttpContextAccessor();

// Hosting behind a proxy: use the real https address and visitor IP it passes on, so QR codes,
// invitation links and reset links show the right address. On hosts like Render, Railway, Fly.io,
// Azure (Linux) or Docker, set Kaon:BehindProxy = true.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue<bool>("Kaon:BehindProxy"))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});
builder.Services.AddHostedService<AutomationService>();
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

        // The Kaon team's pages send a signed-out visitor to the admin login, not the shared one
        var defaultRedirect = options.Events.OnRedirectToLogin;
        options.Events.OnRedirectToLogin = context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/Admin") || path.StartsWithSegments("/AdminBilling"))
                context.RedirectUri = "/admin/login";
            return defaultRedirect(context);
        };

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

// Where payment proofs and QR pictures are kept. On Azure point this at the persistent disk (for example /home/data)
// so they survive a new deployment.
ProofStorage.Root = builder.Configuration["Kaon:DataDir"];

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KaonDbContext>();

    // SQLite does not create the folder of its file, so make it (for example /home/data on Azure)
    var dbFile = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
    var dbFolder = string.IsNullOrWhiteSpace(dbFile) ? null : Path.GetDirectoryName(Path.GetFullPath(dbFile));
    if (!string.IsNullOrEmpty(dbFolder)) Directory.CreateDirectory(dbFolder);

    db.Database.Migrate();

    // One sample carinderia (Jamaica Foods) with starter dishes and food packages is made ONLY
    //  - when developing locally, or
    //  - on a real site when Kaon:Seed:SampleCarinderia is true (and only if there are no carinderias yet).
    // Otherwise everything starts empty: carinderias sign up themselves.
    var seedOwnerEmail = builder.Configuration["Kaon:Seed:OwnerEmail"];
    var seedOwnerPassword = builder.Configuration["Kaon:Seed:OwnerPassword"];
    var seedSample = app.Environment.IsDevelopment() ||
        (builder.Configuration.GetValue<bool>("Kaon:Seed:SampleCarinderia") &&
         !string.IsNullOrWhiteSpace(seedOwnerEmail) && !string.IsNullOrWhiteSpace(seedOwnerPassword));
    var firstRun = false;
    if (seedSample && !db.StoreSettings.Any())
    {
        db.StoreSettings.Add(new StoreSettings
        {
            DeliveryFee = builder.Configuration.GetValue<decimal?>("Kaon:DeliveryFee") ?? 30
        });
        db.SaveChanges();
        firstRun = true;
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

    // Locally this runs at every start (it also upgrades older databases); on a real site only once, when the sample is created
    if (app.Environment.IsDevelopment() || firstRun)
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
    // On a real site: the owner of the one sample carinderia, with the email and password from settings
    else if (firstRun)
    {
        var owner = new AppUser { FullName = "Jamaica Foods Owner", Email = seedOwnerEmail!.Trim().ToLower(), Role = "Owner", StoreId = db.StoreSettings.OrderBy(x => x.Id).First().Id };
        owner.PasswordHash = hasher.HashPassword(owner, seedOwnerPassword!);
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

app.UseForwardedHeaders();

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