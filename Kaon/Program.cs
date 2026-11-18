using Kaon.Data;
using Kaon.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<KaonDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("KaonDb")));

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

builder.Services.AddAuthentication("KaonCookie")
    .AddCookie("KaonCookie", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
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

    DemoData.Seed(db);

    if (!db.Users.Any(u => u.Role == "Owner"))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var owner = new AppUser { FullName = "Kaon Owner", Email = "owner@kaon.local", Role = "Owner" };
        owner.PasswordHash = hasher.HashPassword(owner, "Owner123!");
        db.Users.Add(owner);
        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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