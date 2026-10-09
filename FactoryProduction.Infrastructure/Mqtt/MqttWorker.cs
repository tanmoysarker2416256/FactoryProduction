using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FactoryProduction.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace FactoryProduction.Infrastructure;

/// <summary>
/// mqtt module worker. Outbound MQTT client: subscribes to fse-01/{candidate}/challenge, hands each message to
/// MqttChallengeHandler (same event service as REST) and publishes the result to .../response.
/// Publishes ONLINE after subscribing, HEARTBEAT periodically, OFFLINE as last will. Reconnects with backoff.
/// </summary>
public sealed class MqttWorker(
    IOptions<MqttOptions> options,
    IServiceScopeFactory scopes,
    IMqttStatusTracker tracker,
    IClock clock,
    ILogger<MqttWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("MQTT worker is disabled (Mqtt:Enabled=false).");
            return;
        }
        if (string.IsNullOrWhiteSpace(opt.CandidateId))
        {
            tracker.SetError("Mqtt:CandidateId is not configured", clock.UtcNow);
            logger.LogError("Mqtt:CandidateId is not configured; the MQTT worker will not start.");
            return;
        }

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunSessionAsync(opt, stoppingToken))
                    delay = TimeSpan.FromSeconds(1); // we were connected: start backoff over
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                tracker.SetConnected(false);
                tracker.SetError($"MQTT connection: {ex.Message}", clock.UtcNow);
                logger.LogWarning(ex, "MQTT session failed; retrying in {Delay}s", delay.TotalSeconds);
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    /// <summary>One connection lifetime. Returns true if the broker connection was established.</summary>
    private async Task<bool> RunSessionAsync(MqttOptions opt, CancellationToken stop)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();

        var inbox = Channel.CreateUnbounded<string>();
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The receive callback only queues the text; a separate loop does the work and publishes the reply
        // (publishing from inside the callback could block the client's own receive loop).
        client.ApplicationMessageReceivedAsync += e =>
        {
            inbox.Writer.TryWrite(e.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            lost.TrySetResult();
            return Task.CompletedTask;
        };

        var clientId = $"fse01-{opt.CandidateId}-{Guid.NewGuid().ToString("N")[..6]}";
        var connectOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(opt.Host, opt.Port)
            .WithClientId(clientId)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(opt.StatusTopic)
            .WithWillPayload(Encoding.UTF8.GetBytes(StatusPayload(opt, "OFFLINE")))
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(false)
            .Build();

        await client.ConnectAsync(connectOptions, stop);

        await client.SubscribeAsync(
            factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(opt.ChallengeTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build(),
            stop);

        tracker.SetConnected(true);
        logger.LogInformation("MQTT connected as {ClientId}; subscribed to {Topic}", clientId, opt.ChallengeTopic);
        await PublishAsync(client, opt.StatusTopic, StatusPayload(opt, "ONLINE"), stop);
        tracker.SetHeartbeat(clock.UtcNow);

        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var processor = ProcessInboxAsync(client, inbox.Reader, opt, session.Token);

        try
        {
            while (!stop.IsCancellationRequested && client.IsConnected)
            {
                var winner = await Task.WhenAny(lost.Task, Task.Delay(TimeSpan.FromSeconds(opt.HeartbeatSeconds), stop));
                if (stop.IsCancellationRequested || winner == lost.Task || !client.IsConnected) break;

                await PublishAsync(client, opt.StatusTopic, StatusPayload(opt, "HEARTBEAT"), stop);
                tracker.SetHeartbeat(clock.UtcNow);
            }
        }
        finally
        {
            session.Cancel();
            inbox.Writer.TryComplete();
            try { await processor; } catch (OperationCanceledException) { }
            tracker.SetConnected(false);

            if (stop.IsCancellationRequested && client.IsConnected)
            {
                try
                {
                    await PublishAsync(client, opt.StatusTopic, StatusPayload(opt, "OFFLINE"), CancellationToken.None);
                    await client.DisconnectAsync();
                }
                catch (Exception ex) { logger.LogDebug(ex, "Graceful MQTT shutdown failed"); }
            }
        }
        return true;
    }

    private async Task ProcessInboxAsync(IMqttClient client, ChannelReader<string> reader, MqttOptions opt, CancellationToken ct)
    {
        try
        {
            await foreach (var payload in reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<MqttChallengeHandler>();
                    var result = await handler.HandleMqttChallengeAsync(payload, ct);
                    await PublishAsync(client, opt.ResponseTopic, result.ResponseJson, ct);
                    logger.LogInformation("Challenge {Id} -> {Status} {Code}{Replay}", result.ChallengeId, result.Status,
                        result.ErrorCode, result.Replayed ? " (replayed stored response)" : "");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    tracker.SetError($"Challenge handling failed: {ex.Message}", clock.UtcNow);
                    logger.LogError(ex, "Could not handle or answer an MQTT challenge");
                }
            }
        }
        catch (OperationCanceledException) { /* session ended */ }
    }

    private static Task PublishAsync(IMqttClient client, string topic, string json, CancellationToken ct) =>
        client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build(),
            ct);

    private string StatusPayload(MqttOptions opt, string status) =>
        JsonSerializer.Serialize(new
        {
            candidate_id = opt.CandidateId,
            status,
            timestamp = clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        });
}
