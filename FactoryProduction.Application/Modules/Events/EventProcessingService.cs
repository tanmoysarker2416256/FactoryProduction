using System.Text.Json;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using FactoryProduction.Domain.Events;

namespace FactoryProduction.Application;

/// <summary>
/// events module. This is the ONLY place where COUNT / VOID rules live. REST and MQTT both call
/// ProcessBatchAsync / ProcessEventAsync, so a rule changed here changes both channels.
///
///   validate_event() -> process_event() -> process_count() | process_void() -> resolve_pending_voids()
///
/// Each item runs in its own database transaction behind one application lock, so concurrent
/// requests are processed one after another (no double counting, no lost pending VOID).
/// </summary>
public sealed class EventProcessingService(
    IEventRepository events,
    ISubmissionRepository attempts,
    IUnitOfWork uow,
    IClock clock,
    IDomainEventPublisher publisher)
{
    public const string LockName = "fse01:event-processing";

    /// <summary>Items are processed in submitted order; the response keeps the same order.</summary>
    public async Task<ProcessEventsResponse> ProcessBatchAsync(IReadOnlyList<JsonElement> items, string channel, CancellationToken ct)
    {
        var results = new List<EventResult>(items.Count);
        foreach (var item in items)
            results.Add(await ProcessEventAsync(item, channel, ct));
        return new ProcessEventsResponse(results);
    }

    /// <summary>process_event(): validate, then run the matching rule inside one transaction.</summary>
    public async Task<EventResult> ProcessEventAsync(JsonElement item, string channel, CancellationToken ct)
    {
        var raw = item.GetRawText();
        var outcome = EventValidator.ValidateEvent(item);
        var now = clock.UtcNow;

        if (!outcome.IsValid)
        {
            return await uow.ExecuteInTransactionAsync(async () =>
            {
                await attempts.AddAsync(NewAttempt(channel, raw, outcome.SourceId, outcome.EventId, null,
                    ItemStatuses.Rejected, outcome.ErrorText, now), ct);
                return new EventResult(outcome.EventId, ItemStatuses.Rejected, outcome.ErrorText);
            }, null, ct);
        }

        var data = outcome.Data!;
        var raised = new List<IDomainEvent>();
        var result = await uow.ExecuteInTransactionAsync(
            () => ProcessValidatedAsync(data, raw, channel, now, raised, ct), LockName, ct);

        // Notifications only after the database work has committed.
        await publisher.PublishAsync(raised, ct);
        return result;
    }

    private async Task<EventResult> ProcessValidatedAsync(
        EventData data, string raw, string channel, DateTimeOffset now, List<IDomainEvent> raised, CancellationToken ct)
    {
        var existing = await events.FindByEventIdAsync(data.EventId, ct);
        if (existing is not null)
        {
            var same = existing.Matches(data);
            var status = same ? ItemStatuses.Duplicate : ItemStatuses.Conflict;
            var message = same
                ? "Duplicate of an event already stored; not processed again"
                : "Event ID already used with different data; original event preserved";
            await attempts.AddAsync(NewAttempt(channel, raw, data.SourceId, data.EventId, data, status, same ? null : message, now), ct);
            return new EventResult(data.EventId, status, message);
        }

        await events.EnsureSourceAsync(data.SourceId, now, ct);

        return data.Type == EventTypes.Count
            ? await ProcessCountAsync(data, raw, channel, now, raised, ct)
            : await ProcessVoidAsync(data, raw, channel, now, raised, ct);
    }

    /// <summary>process_count(): a COUNT adds its quantity exactly once.</summary>
    private async Task<EventResult> ProcessCountAsync(
        EventData data, string raw, string channel, DateTimeOffset now, List<IDomainEvent> raised, CancellationToken ct)
    {
        var count = ProductionEvent.Create(data, ItemStatuses.Accepted, now);
        await events.AddAsync(count, ct);
        await attempts.AddAsync(NewAttempt(channel, raw, data.SourceId, data.EventId, data, ItemStatuses.Accepted, null, now), ct);
        await uow.SaveChangesAsync(ct);
        raised.Add(new EventAccepted(data.EventId, data.SourceId, data.Type, now));

        await ResolvePendingVoidsAsync(count, now, raised, ct);
        return new EventResult(data.EventId, ItemStatuses.Accepted, "Event processed");
    }

    /// <summary>process_void(): reverses one accepted COUNT of the same source, or waits for it.</summary>
    private async Task<EventResult> ProcessVoidAsync(
        EventData data, string raw, string channel, DateTimeOffset now, List<IDomainEvent> raised, CancellationToken ct)
    {
        var target = await events.FindByEventIdAsync(data.TargetEventId!, ct);

        if (target is null)
        {
            var reason = $"Waiting for COUNT {data.TargetEventId}";
            await events.AddAsync(ProductionEvent.Create(data, ItemStatuses.PendingReference, now, reason), ct);
            await attempts.AddAsync(NewAttempt(channel, raw, data.SourceId, data.EventId, data, ItemStatuses.PendingReference, reason, now), ct);
            return new EventResult(data.EventId, ItemStatuses.PendingReference,
                $"COUNT {data.TargetEventId} not found yet; VOID stored until it arrives");
        }

        string? rejection = null;
        if (target.Type != EventTypes.Count)
            rejection = $"Target {target.EventId} is not a COUNT event";
        else if (!string.Equals(target.SourceId, data.SourceId, StringComparison.OrdinalIgnoreCase))
            rejection = $"VOID source_id {data.SourceId} does not match COUNT source_id {target.SourceId}";
        else
        {
            var already = await events.FindAcceptedVoidForAsync(target.EventId, ct);
            if (already is not null)
                rejection = $"COUNT {target.EventId} was already reversed by VOID {already.EventId}";
        }

        if (rejection is not null)
        {
            await attempts.AddAsync(NewAttempt(channel, raw, data.SourceId, data.EventId, data, ItemStatuses.Rejected, rejection, now), ct);
            return new EventResult(data.EventId, ItemStatuses.Rejected, rejection);
        }

        var voidEvent = ProductionEvent.Create(data, ItemStatuses.Accepted, now);
        voidEvent.AutoAcknowledge(now); // VOIDs are correction records: auto-acknowledged
        await events.AddAsync(voidEvent, ct);
        await attempts.AddAsync(NewAttempt(channel, raw, data.SourceId, data.EventId, data, ItemStatuses.Accepted, null, now), ct);
        raised.Add(new EventAccepted(data.EventId, data.SourceId, data.Type, now));
        raised.Add(new EventAcknowledged(data.EventId, data.SourceId, true, now));
        return new EventResult(data.EventId, ItemStatuses.Accepted, "Event processed");
    }

    /// <summary>
    /// resolve_pending_voids(): a COUNT just arrived. The first stored VOID of the same source wins and is applied;
    /// every other waiting VOID for this COUNT is rejected with a clear reason.
    /// </summary>
    private async Task ResolvePendingVoidsAsync(ProductionEvent count, DateTimeOffset now, List<IDomainEvent> raised, CancellationToken ct)
    {
        var waiting = await events.FindPendingVoidsForAsync(count.EventId, ct); // ordered by storage order
        if (waiting.Count == 0) return;

        string? winnerId = null;
        foreach (var pending in waiting)
        {
            if (!string.Equals(pending.SourceId, count.SourceId, StringComparison.OrdinalIgnoreCase))
            {
                var reason = $"VOID source_id {pending.SourceId} does not match COUNT source_id {count.SourceId}";
                pending.Reject(reason, now);
                raised.Add(new VoidRejected(pending.EventId, pending.SourceId, reason, now));
            }
            else if (winnerId is null)
            {
                pending.ResolveAsAccepted(now);
                winnerId = pending.EventId;
                raised.Add(new VoidResolved(pending.EventId, pending.SourceId, count.EventId, now));
                raised.Add(new EventAcknowledged(pending.EventId, pending.SourceId, true, now));
            }
            else
            {
                var reason = $"COUNT {count.EventId} is already reversed by VOID {winnerId} (first stored VOID wins)";
                pending.Reject(reason, now);
                raised.Add(new VoidRejected(pending.EventId, pending.SourceId, reason, now));
            }
        }
    }

    private static SubmissionAttempt NewAttempt(
        string channel, string raw, string? sourceId, string? eventId, EventData? data,
        string classification, string? error, DateTimeOffset now) => new()
        {
            Channel = channel,
            RawPayload = raw,
            NormalizedData = data is null ? null : JsonSerializer.Serialize(data, JsonDefaults.Rest),
            SourceId = sourceId,
            EventId = eventId,
            Classification = classification,
            Error = error is { Length: > 500 } ? error[..500] : error,
            ReceivedAt = now
        };
}
