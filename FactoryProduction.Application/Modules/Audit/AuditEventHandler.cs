using System.Text.Json;
using FactoryProduction.Domain.Entities;
using FactoryProduction.Domain.Events;

namespace FactoryProduction.Application;

/// <summary>audit module: writes every business notification to the audit log.</summary>
public sealed class AuditEventHandler(IAuditRepository repository) : IDomainEventHandler
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct) =>
        repository.AddAsync(new AuditEntry
        {
            Name = domainEvent.Name,
            EventId = domainEvent.EventId,
            SourceId = domainEvent.SourceId,
            Details = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonDefaults.Rest),
            OccurredAt = domainEvent.OccurredAt
        }, ct);
}
