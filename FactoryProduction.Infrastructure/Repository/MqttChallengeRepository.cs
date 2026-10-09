using FactoryProduction.Application;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FactoryProduction.Infrastructure;

public sealed class MqttChallengeRepository(AppDbContext db) : IMqttChallengeRepository
{
    public Task<MqttChallenge?> FindAsync(string challengeId, CancellationToken ct) =>
        db.MqttChallenges.AsNoTracking().FirstOrDefaultAsync(c => c.ChallengeId == challengeId, ct);

    public async Task<bool> TryAddAsync(MqttChallenge challenge, CancellationToken ct)
    {
        try
        {
            db.MqttChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // primary-key clash: another writer stored this challenge first
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<MqttChallengeStats> GetStatsAsync(CancellationToken ct)
    {
        var all = db.MqttChallenges.AsNoTracking();
        var total = await all.CountAsync(ct);
        var completed = await all.CountAsync(c => c.Status == MqttStatuses.Completed, ct);
        var last = await all.OrderByDescending(c => c.ReceivedAt).FirstOrDefaultAsync(ct);
        var lastFailure = await all.Where(c => c.Status == MqttStatuses.Failed)
            .OrderByDescending(c => c.ReceivedAt)
            .Select(c => c.ErrorCode + ": " + c.ErrorMessage)
            .FirstOrDefaultAsync(ct);

        return new MqttChallengeStats(total, completed, total - completed,
            last?.ChallengeId, last?.ReceivedAt, last?.Status, lastFailure);
    }
}
