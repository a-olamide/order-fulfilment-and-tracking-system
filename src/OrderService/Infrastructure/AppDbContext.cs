using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace OrderService.Infrastructure
{
    public sealed class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.CustomerEmail);

            base.OnModelCreating(modelBuilder);
        }
    }
}
