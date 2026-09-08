using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Data;

/// <summary>
/// Inherits from IdentityDbContext so the admin login shares one database and
/// one migration history with the shop tables.
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<PickupLocation> PickupLocations => Set<PickupLocation>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Price).HasPrecision(10, 3); // JOD carries 3 decimals
        });

        b.Entity<ProductImage>()
            .HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Order>(e =>
        {
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.Total).HasPrecision(10, 3);

            // Never let deleting a pickup location take orders with it.
            e.HasOne(o => o.PickupLocation)
             .WithMany()
             .HasForeignKey(o => o.PickupLocationId)
             .OnDelete(DeleteBehavior.Restrict);

            // Deleting an account must not delete what that person ordered —
            // the admin's own delete-user screen promises past orders survive,
            // and the order carries its own copy of the name and phone anyway.
            e.HasOne(o => o.User)
             .WithMany()
             .HasForeignKey(o => o.UserId)
             .OnDelete(DeleteBehavior.SetNull);

            // Both lookups behind "my previous requests": by account, and by
            // phone for orders placed before that person registered.
            e.HasIndex(o => o.UserId);
            e.HasIndex(o => o.Phone);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPriceSnapshot).HasPrecision(10, 3);
            e.Property(i => i.SizeKg).HasPrecision(6, 3);   // 0.5, 1, …

            e.HasOne(i => i.Order)
             .WithMany(o => o.Items)
             .HasForeignKey(i => i.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            // A retired product must not erase the line that references it —
            // the snapshot columns keep the order readable either way.
            e.HasOne(i => i.Product)
             .WithMany()
             .HasForeignKey(i => i.ProductId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
