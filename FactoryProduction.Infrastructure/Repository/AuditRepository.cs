using FactoryProduction.Application;
using FactoryProduction.Domain.Entities;

namespace FactoryProduction.Infrastructure;

public sealed class AuditRepository(AppDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditEntry entry, CancellationToken ct)
    {
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        db.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
    }
}
