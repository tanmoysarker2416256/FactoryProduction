using Microsoft.Extensions.Options;

namespace FactoryProduction.Application;

/// <summary>Facts for the dashboard's MQTT card: live connection + durable challenge history.</summary>
public sealed class MqttStatusService(
    IMqttChallengeRepository challenges, IMqttStatusTracker tracker, IOptions<MqttOptions> options)
{
    public async Task<MqttStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var o = options.Value;
        var stats = await challenges.GetStatsAsync(ct);
        var live = tracker.Snapshot();

        return new MqttStatusResponse(
            o.Enabled, live.Connected, $"{o.Host}:{o.Port}", o.CandidateId,
            o.ChallengeTopic, o.ResponseTopic, o.StatusTopic,
            stats.LastChallengeId, stats.LastReceivedAt, stats.LastStatus,
            stats.Total, stats.Completed, stats.Failed,
            live.LastError ?? stats.LastFailure, live.LastErrorAt, live.LastHeartbeatAt);
    }
}
