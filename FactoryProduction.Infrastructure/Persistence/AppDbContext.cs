using FactoryProduction.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace FactoryProduction.Infrastructure;

/// <summary>shared/db: one SQL Server database for every module. Tables are created by /migrations/*.sql.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProductionSource> ProductionSources => Set<ProductionSource>();
    public DbSet<ProductionEvent> ProductionEvents => Set<ProductionEvent>();
    public DbSet<SubmissionAttempt> SubmissionAttempts => Set<SubmissionAttempt>();
    public DbSet<MqttChallenge> MqttChallenges => Set<MqttChallenge>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<ProductionSource>(b =>
        {
            b.ToTable("production_sources");
            b.HasKey(x => x.SourceId);
            b.Property(x => x.SourceId).HasMaxLength(100).ValueGeneratedNever();
            b.Property(x => x.DisplayName).HasMaxLength(200);
        });

        mb.Entity<ProductionEvent>(b =>
        {
            b.ToTable("production_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.EventId).HasMaxLength(100);
            b.Property(x => x.SourceId).HasMaxLength(100);
            b.Property(x => x.Type).HasMaxLength(10).IsUnicode(false);
            b.Property(x => x.TargetEventId).HasMaxLength(100);
            b.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            b.Property(x => x.FailureReason).HasMaxLength(500);
        });

        mb.Entity<SubmissionAttempt>(b =>
        {
            b.ToTable("submission_attempts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(10).IsUnicode(false);
            b.Property(x => x.SourceId).HasMaxLength(100);
            b.Property(x => x.EventId).HasMaxLength(100);
            b.Property(x => x.Classification).HasMaxLength(30).IsUnicode(false);
            b.Property(x => x.Error).HasMaxLength(500);
        });

        mb.Entity<MqttChallenge>(b =>
        {
            b.ToTable("mqtt_challenges");
            b.HasKey(x => x.ChallengeId);
            b.Property(x => x.ChallengeId).HasMaxLength(100).ValueGeneratedNever();
            b.Property(x => x.RequestDigest).HasMaxLength(64).IsUnicode(false);
            b.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            b.Property(x => x.ErrorCode).HasMaxLength(50).IsUnicode(false);
            b.Property(x => x.ErrorMessage).HasMaxLength(500);
        });

        mb.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(60).IsUnicode(false);
            b.Property(x => x.EventId).HasMaxLength(100);
            b.Property(x => x.SourceId).HasMaxLength(100);
        });

        // C# PascalCase -> SQL snake_case column names (EventId -> event_id).
        foreach (var entity in mb.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant());
    }
}
