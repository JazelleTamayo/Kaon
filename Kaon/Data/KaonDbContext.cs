using Kaon.Models;
using Microsoft.EntityFrameworkCore;

namespace Kaon.Data;

public class KaonDbContext : DbContext
{
    public KaonDbContext(DbContextOptions<KaonDbContext> options) : base(options) { }

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<ItemOption> ItemOptions => Set<ItemOption>();
    public DbSet<PasswordToken> PasswordTokens => Set<PasswordToken>();
    public DbSet<DailyMenuEntry> DailyMenuEntries => Set<DailyMenuEntry>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<CarinderiaRequest> CarinderiaRequests => Set<CarinderiaRequest>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<QrScan> QrScans => Set<QrScan>();
    public DbSet<Rider> Riders => Set<Rider>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The same email can be a customer at two different carinderias (separate accounts); never twice at one
        modelBuilder.Entity<AppUser>().HasIndex(u => new { u.Email, u.StoreId }).IsUnique();

        modelBuilder.Entity<StoreSettings>().HasIndex(s => s.Slug).IsUnique();
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.At);
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.StoreId);
        modelBuilder.Entity<MenuItem>().HasIndex(m => m.StoreId);
        modelBuilder.Entity<Order>().HasIndex(o => o.StoreId);
        modelBuilder.Entity<AppUser>().HasIndex(u => u.StoreId);

        modelBuilder.Entity<PasswordToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<PasswordToken>()
            .HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Subscription>().HasIndex(x => x.OwnerId).IsUnique();
        modelBuilder.Entity<QrScan>().HasIndex(x => new { x.StoreId, x.At });
        modelBuilder.Entity<Rider>().HasIndex(x => x.StoreId);
        modelBuilder.Entity<Order>().HasIndex(x => x.RiderToken).IsUnique();
        modelBuilder.Entity<Subscription>()
            .HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SubscriptionPayment>()
            .HasOne(x => x.Subscription).WithMany(x => x.Payments)
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ItemOption>()
            .HasOne(o => o.MenuItem).WithMany(m => m.Options)
            .HasForeignKey(o => o.MenuItemId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DailyMenuEntry>()
            .HasOne(e => e.MenuItem).WithMany()
            .HasForeignKey(e => e.MenuItemId).OnDelete(DeleteBehavior.Cascade);
    }
}