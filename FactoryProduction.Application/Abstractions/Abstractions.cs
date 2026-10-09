using FactoryProduction.Domain.Entities;
using FactoryProduction.Domain.Events;

namespace FactoryProduction.Application;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Runs work in one database transaction, optionally behind an exclusive named lock.</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, string? exclusiveLock, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

// ---- events module ----
public interface IEventRepository
{
    Task<ProductionEvent?> FindByEventIdAsync(string eventId, CancellationToken ct);
    Task<ProductionEvent?> FindAcceptedVoidForAsync(string targetEventId, CancellationToken ct);
    Task<IReadOnlyList<ProductionEvent>> FindPendingVoidsForAsync(string targetEventId, CancellationToken ct);
    Task EnsureSourceAsync(string sourceId, DateTimeOffset now, CancellationToken ct);
    Task AddAsync(ProductionEvent productionEvent, CancellationToken ct);
}

public interface ISubmissionRepository
{
    Task AddAsync(SubmissionAttempt attempt, CancellationToken ct);
}

// ---- ack module ----
public interface IAckRepository
{
    /// <summary>Atomic "set acknowledged_at if still null". True only for the caller that actually changed it.</summary>
    Task<bool> TryAcknowledgeAsync(string eventId, DateTimeOffset now, CancellationToken ct);
    Task<ProductionEvent?> FindAsync(string eventId, CancellationToken ct);
}

// ---- state module ----
public interface IStateQueries
{
    Task<StateSummary> GetSummaryAsync(string? sourceId, CancellationToken ct);
    Task<IReadOnlyList<PendingItem>> GetPendingAsync(string? sourceId, CancellationToken ct);
    Task<IReadOnlyList<ExceptionItem>> GetExceptionsAsync(string? sourceId, CancellationToken ct);
}

// ---- mqtt module ----
public sealed record MqttChallengeStats(
    int Total, int Completed, int Failed,
    string? LastChallengeId, DateTimeOffset? LastReceivedAt, string? LastStatus, string? LastFailure);

public interface IMqttChallengeRepository
{
    Task<MqttChallenge?> FindAsync(string challengeId, CancellationToken ct);
    /// <summary>Returns false when another writer stored the same challenge_id first.</summary>
    Task<bool> TryAddAsync(MqttChallenge challenge, CancellationToken ct);
    Task<MqttChallengeStats> GetStatsAsync(CancellationToken ct);
}

// ---- audit module ----
public interface IAuditRepository
{
    Task AddAsync(AuditEntry entry, CancellationToken ct);
}

// ---- internal notifications (could later be a message broker) ----
public interface IDomainEventPublisher
{
    Task PublishAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct);
}

public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct);
}
