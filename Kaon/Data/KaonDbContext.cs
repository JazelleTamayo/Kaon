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
    public DbSet<CustomerStore> CustomerStores => Set<CustomerStore>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<StoreSettings>().HasIndex(s => s.Slug).IsUnique();
        modelBuilder.Entity<MenuItem>().HasIndex(m => m.StoreId);
        modelBuilder.Entity<Order>().HasIndex(o => o.StoreId);
        modelBuilder.Entity<AppUser>().HasIndex(u => u.StoreId);
        modelBuilder.Entity<CustomerStore>().HasIndex(c => new { c.UserId, c.StoreId }).IsUnique();

        modelBuilder.Entity<PasswordToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<PasswordToken>()
            .HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Subscription>().HasIndex(x => x.OwnerId).IsUnique();
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