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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<ItemOption>()
            .HasOne(o => o.MenuItem).WithMany(m => m.Options)
            .HasForeignKey(o => o.MenuItemId).OnDelete(DeleteBehavior.Cascade);
    }
}