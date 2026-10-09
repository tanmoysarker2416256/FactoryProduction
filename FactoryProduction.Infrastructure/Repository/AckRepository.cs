using FactoryProduction.Application;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FactoryProduction.Infrastructure;

public sealed class AckRepository(AppDbContext db) : IAckRepository
{
    public async Task<bool> TryAcknowledgeAsync(string eventId, DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset? stamp = now;
        var rows = await db.ProductionEvents
            .Where(e => e.EventId == eventId && e.Status == ItemStatuses.Accepted && e.AcknowledgedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AcknowledgedAt, stamp), ct);
        return rows == 1;
    }

    public Task<ProductionEvent?> FindAsync(string eventId, CancellationToken ct) =>
        db.ProductionEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
}
