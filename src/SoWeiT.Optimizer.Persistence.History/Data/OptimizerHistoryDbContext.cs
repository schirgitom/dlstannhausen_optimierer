using Microsoft.EntityFrameworkCore;

namespace SoWeiT.Optimizer.Persistence.History.Data;

public sealed class OptimizerHistoryDbContext : DbContext
{
    public OptimizerHistoryDbContext(DbContextOptions<OptimizerHistoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<OptimizerSessionEntry> Sessions => Set<OptimizerSessionEntry>();

    public DbSet<OptimizerRequestEntry> Requests => Set<OptimizerRequestEntry>();

    public DbSet<OptimizerRequestUserEntry> RequestUsers => Set<OptimizerRequestUserEntry>();

    public DbSet<CustomerEntry> Customers => Set<CustomerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var customers = modelBuilder.Entity<CustomerEntry>();
        customers.ToTable("customers");
        customers.HasKey(x => x.Id);
        customers.Property(x => x.Id).ValueGeneratedOnAdd();
        customers.Property(x => x.Name).HasMaxLength(256).IsRequired();
        customers.Property(x => x.CustomerNumber).HasMaxLength(64);
        customers.Property(x => x.CreatedAtUtc).IsRequired();
        customers.HasIndex(x => x.Name).IsUnique();

        var sessions = modelBuilder.Entity<OptimizerSessionEntry>();
        sessions.ToTable("optimizer_sessions");
        sessions.HasKey(x => x.SessionId);
        sessions.Property(x => x.CreatedAtUtc).IsRequired();
        sessions.Property(x => x.N).IsRequired();
        sessions.Property(x => x.Sperrzeit1).IsRequired();
        sessions.Property(x => x.Sperrzeit2).IsRequired();
        sessions.Property(x => x.UseOrTools).IsRequired();
        sessions.Property(x => x.UseGreedyFallback).IsRequired();
        sessions.HasIndex(x => x.CreatedAtUtc);
        sessions.HasIndex(x => x.EndedAtUtc);

        var requests = modelBuilder.Entity<OptimizerRequestEntry>();
        requests.ToTable("optimizer_requests");
        requests.HasKey(x => x.Id);
        requests.Property(x => x.Id).ValueGeneratedOnAdd();
        requests.Property(x => x.SessionId).IsRequired();
        requests.Property(x => x.RequestType).HasMaxLength(128).IsRequired();
        requests.Property(x => x.RequestTimestamp).IsRequired();
        requests.Property(x => x.CreatedAtUtc).IsRequired();
        requests.HasIndex(x => x.SessionId);
        requests.HasIndex(x => x.RequestTimestamp);
        requests.HasOne(x => x.Session)
            .WithMany(x => x.Requests)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        var users = modelBuilder.Entity<OptimizerRequestUserEntry>();
        users.ToTable("optimizer_request_users");
        users.HasKey(x => x.Id);
        users.Property(x => x.Id).ValueGeneratedOnAdd();
        users.Property(x => x.RequestEntryId).IsRequired();
        users.Property(x => x.UserIndex).IsRequired();
        users.Property(x => x.CustomerId).IsRequired();
        users.Property(x => x.RequiredPowerWatt).IsRequired();
        users.Property(x => x.IsSwitchAllowed).IsRequired();
        users.HasIndex(x => x.RequestEntryId);
        users.HasIndex(x => x.CustomerId);
        users.HasIndex(x => new { x.RequestEntryId, x.UserIndex }).IsUnique();
        users.HasOne(x => x.Request)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.RequestEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        users.HasOne(x => x.Customer)
            .WithMany(x => x.RequestUsers)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

