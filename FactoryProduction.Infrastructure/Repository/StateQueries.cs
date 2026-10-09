using FactoryProduction.Application;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FactoryProduction.Infrastructure;

/// <summary>All dashboard numbers come from the database (durable evidence), never from process memory.</summary>
public sealed class StateQueries(AppDbContext db) : IStateQueries
{
    private const int MaxRows = 500;

    private IQueryable<ProductionEvent> EventsFor(string? sourceId) =>
        db.ProductionEvents.AsNoTracking().Where(e => sourceId == null || e.SourceId == sourceId);

    public async Task<StateSummary> GetSummaryAsync(string? sourceId, CancellationToken ct)
    {
        var accepted = EventsFor(sourceId).Where(e => e.Status == ItemStatuses.Accepted);

        var counted = await accepted.Where(e => e.Type == EventTypes.Count)
            .SumAsync(e => (long?)e.Quantity, ct) ?? 0;

        // A VOID subtracts the quantity of the COUNT it reverses.
        var voided = await (
            from v in accepted.Where(e => e.Type == EventTypes.Void)
            join c in db.ProductionEvents.AsNoTracking() on v.TargetEventId equals c.EventId
            select (long?)c.Quantity).SumAsync(ct) ?? 0;

        var processed = await accepted.CountAsync(ct);
        var pendingAck = await accepted.CountAsync(e => e.AcknowledgedAt == null, ct);
        var unresolved = await EventsFor(sourceId).CountAsync(e => e.Status == ItemStatuses.PendingReference, ct);

        var attempts = db.SubmissionAttempts.AsNoTracking().Where(a => sourceId == null || a.SourceId == sourceId);
        var duplicates = await attempts.CountAsync(a => a.Classification == ItemStatuses.Duplicate, ct);
        var conflicts = await attempts.CountAsync(a => a.Classification == ItemStatuses.Conflict, ct);

        return new StateSummary(counted - voided, processed, pendingAck, unresolved, duplicates, conflicts);
    }

    public async Task<IReadOnlyList<PendingItem>> GetPendingAsync(string? sourceId, CancellationToken ct)
    {
        var rows = await EventsFor(sourceId)
            .Where(e => e.Status == ItemStatuses.Accepted && e.AcknowledgedAt == null)
            .OrderBy(e => e.EventTime).ThenBy(e => e.Id)
            .Take(MaxRows)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.EventId).ToList();
        var voidedIds = (await db.ProductionEvents.AsNoTracking()
                .Where(v => v.Type == EventTypes.Void && v.Status == ItemStatuses.Accepted && ids.Contains(v.TargetEventId!))
                .Select(v => v.TargetEventId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return rows.Select(r => new PendingItem(
            r.EventId, r.SourceId, r.Type, r.Quantity, r.TargetEventId,
            r.EventTime, r.ReceivedAt, r.Status, voidedIds.Contains(r.EventId))).ToList();
    }

    public async Task<IReadOnlyList<ExceptionItem>> GetExceptionsAsync(string? sourceId, CancellationToken ct)
    {
        var storedProblems = await EventsFor(sourceId)
            .Where(e => e.Status == ItemStatuses.PendingReference || e.Status == ItemStatuses.Rejected)
            .OrderByDescending(e => e.Id).Take(MaxRows)
            .ToListAsync(ct);

        var attemptProblems = await db.SubmissionAttempts.AsNoTracking()
            .Where(a => (sourceId == null || a.SourceId == sourceId)
                        && (a.Classification == ItemStatuses.Rejected || a.Classification == ItemStatuses.Conflict))
            .OrderByDescending(a => a.Id).Take(MaxRows)
            .ToListAsync(ct);

        var items = new List<ExceptionItem>();
        items.AddRange(storedProblems.Select(e => new ExceptionItem(
            e.Status == ItemStatuses.PendingReference ? "UNRESOLVED_REFERENCE" : "REJECTED_VOID",
            e.EventId, e.SourceId, e.Status,
            e.FailureReason ?? $"Waiting for COUNT {e.TargetEventId}",
            e.ResolvedAt ?? e.ReceivedAt, null)));
        items.AddRange(attemptProblems.Select(a => new ExceptionItem(
            a.Classification == ItemStatuses.Conflict ? "CONFLICT" : "REJECTED_SUBMISSION",
            a.EventId, a.SourceId, a.Classification,
            a.Error ?? "No reason recorded", a.ReceivedAt, a.RawPayload)));

        return items.OrderByDescending(i => i.OccurredAt).Take(MaxRows).ToList();
    }
}
