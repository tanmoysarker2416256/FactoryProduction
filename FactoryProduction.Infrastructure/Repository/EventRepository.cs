using FactoryProduction.Application;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FactoryProduction.Infrastructure;

public sealed class EventRepository(AppDbContext db) : IEventRepository
{
    public Task<ProductionEvent?> FindByEventIdAsync(string eventId, CancellationToken ct) =>
        db.ProductionEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);

    public Task<ProductionEvent?> FindAcceptedVoidForAsync(string targetEventId, CancellationToken ct) =>
        db.ProductionEvents.AsNoTracking().FirstOrDefaultAsync(
            e => e.Type == EventTypes.Void && e.Status == ItemStatuses.Accepted && e.TargetEventId == targetEventId, ct);

    public async Task<IReadOnlyList<ProductionEvent>> FindPendingVoidsForAsync(string targetEventId, CancellationToken ct) =>
        await db.ProductionEvents
            .Where(e => e.Type == EventTypes.Void && e.Status == ItemStatuses.PendingReference && e.TargetEventId == targetEventId)
            .OrderBy(e => e.Id) // storage order: the first stored VOID wins
            .ToListAsync(ct);

    public async Task EnsureSourceAsync(string sourceId, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.ProductionSources.AnyAsync(s => s.SourceId == sourceId, ct)) return;
        db.ProductionSources.Add(new ProductionSource(sourceId, sourceId, now));
        await db.SaveChangesAsync(ct); // the event row has a foreign key to this row
    }

    public async Task AddAsync(ProductionEvent productionEvent, CancellationToken ct) =>
        await db.ProductionEvents.AddAsync(productionEvent, ct);
}

public sealed class SubmissionRepository(AppDbContext db) : ISubmissionRepository
{
    public async Task AddAsync(SubmissionAttempt attempt, CancellationToken ct) =>
        await db.SubmissionAttempts.AddAsync(attempt, ct);
}
