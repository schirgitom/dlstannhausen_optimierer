using Microsoft.EntityFrameworkCore;

namespace SoWeiT.Optimizer.Api.Data;

public sealed class OptimizerHistoryDbContext : DbContext
{
    public OptimizerHistoryDbContext(DbContextOptions<OptimizerHistoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<OptimizerSessionEntry> Sessions => Set<OptimizerSessionEntry>();

    public DbSet<OptimizerRequestEntry> Requests => Set<OptimizerRequestEntry>();

    public DbSet<OptimizerRequestUserEntry> RequestUsers => Set<OptimizerRequestUserEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        users.Property(x => x.RequiredPowerWatt).IsRequired();
        users.Property(x => x.IsSwitchAllowed).IsRequired();
        users.HasIndex(x => x.RequestEntryId);
        users.HasIndex(x => new { x.RequestEntryId, x.UserIndex }).IsUnique();
        users.HasOne(x => x.Request)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.RequestEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
