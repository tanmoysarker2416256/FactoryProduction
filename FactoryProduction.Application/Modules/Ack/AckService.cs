using FactoryProduction.Domain;
using FactoryProduction.Domain.Events;

namespace FactoryProduction.Application;

/// <summary>ack module: marks successfully processed events as reviewed. Never deletes history.</summary>
public sealed class AckService(IAckRepository repository, IClock clock, IDomainEventPublisher publisher)
{
    /// <summary>One result per requested ID, in the original order. Repeats are safe.</summary>
    public async Task<AckResponse> AcknowledgeEventsAsync(IReadOnlyList<string> eventIds, CancellationToken ct)
    {
        var results = new List<AckResult>(eventIds.Count);
        var raised = new List<IDomainEvent>();
        foreach (var id in eventIds)
            results.Add(await AcknowledgeEventAsync(id, raised, ct));

        await publisher.PublishAsync(raised, ct);
        return new AckResponse(results);
    }

    /// <summary>acknowledge_event(): ACKED / ALREADY_ACKED / NOT_READY / NOT_FOUND.</summary>
    public async Task<AckResult> AcknowledgeEventAsync(string eventId, List<IDomainEvent> raised, CancellationToken ct)
    {
        var id = (eventId ?? string.Empty).Trim();
        if (id.Length == 0)
            return new AckResult(eventId ?? string.Empty, AckStatuses.NotFound);

        var now = clock.UtcNow;

        // One atomic UPDATE ... WHERE acknowledged_at IS NULL: two concurrent callers cannot both win.
        if (await repository.TryAcknowledgeAsync(id, now, ct))
        {
            var acked = await repository.FindAsync(id, ct);
            raised.Add(new EventAcknowledged(acked?.EventId ?? id, acked?.SourceId ?? string.Empty, false, now));
            return new AckResult(id, AckStatuses.Acked);
        }

        var existing = await repository.FindAsync(id, ct);
        if (existing is null) return new AckResult(id, AckStatuses.NotFound);
        if (existing.Status != ItemStatuses.Accepted) return new AckResult(id, AckStatuses.NotReady);
        return new AckResult(id, AckStatuses.AlreadyAcked);
    }
}
