namespace FactoryProduction.Domain.Events;

/// <summary>Internal business notifications. Raised only after the database work has committed.</summary>
public interface IDomainEvent
{
    string Name { get; }
    string EventId { get; }
    string SourceId { get; }
    DateTimeOffset OccurredAt { get; }
}

public sealed record EventAccepted(string EventId, string SourceId, string Type, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Name => "EVENT_ACCEPTED";
}

public sealed record VoidResolved(string EventId, string SourceId, string TargetEventId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Name => "VOID_RESOLVED";
}

public sealed record VoidRejected(string EventId, string SourceId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Name => "VOID_REJECTED";
}

public sealed record EventAcknowledged(string EventId, string SourceId, bool Automatic, DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Name => "EVENT_ACKNOWLEDGED";
}
