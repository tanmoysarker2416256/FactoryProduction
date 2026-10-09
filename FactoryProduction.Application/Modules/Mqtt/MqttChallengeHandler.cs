using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FactoryProduction.Application.Modules.Mqtt;

public sealed record MqttHandlerResult(string? ChallengeId, string Status, string? ErrorCode, string ResponseJson, bool Replayed);

/// <summary>
/// mqtt module: handle_mqtt_challenge(). Validates the envelope, then calls the SAME EventProcessingService
/// and StateService that the REST API uses. Contains no COUNT/VOID rules of its own.
/// </summary>
public sealed class MqttChallengeHandler(
    EventProcessingService eventService,
    StateService stateService,
    IMqttChallengeRepository challenges,
    IClock clock,
    IOptions<MqttOptions> options,
    IMqttStatusTracker tracker,
    ILogger<MqttChallengeHandler> logger)
{
    public const string ProtocolVersion = "1.0";
    public const string Command = "PROCESS_EVENTS";

    public async Task<MqttHandlerResult> HandleMqttChallengeAsync(string payload, CancellationToken ct)
    {
        try
        {
            return await HandleCoreAsync(payload, clock.UtcNow, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while handling an MQTT challenge");
            // Not persisted on purpose: the simulator may retry the same challenge and succeed.
            return Unsaved(TryReadChallengeId(payload), "INTERNAL_ERROR", "Unexpected error while processing the challenge.");
        }
    }

    private async Task<MqttHandlerResult> HandleCoreAsync(string payload, DateTimeOffset received, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return Unsaved(null, "VALIDATION_ERROR", "Challenge is not valid JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Unsaved(null, "VALIDATION_ERROR", "Challenge must be a JSON object.");

            var challengeId = ReadString(root, "challenge_id");
            if (string.IsNullOrWhiteSpace(challengeId))
                return Unsaved(null, "VALIDATION_ERROR", "challenge_id is required.");
            if (challengeId.Length > 100)
                return Unsaved(null, "VALIDATION_ERROR", "challenge_id is too long.");

            var digest = ComputeDigest(payload);

            // Replay safety: same ID + same body -> original response, nothing processed again.
            var existing = await challenges.FindAsync(challengeId, ct);
            if (existing is not null)
                return existing.RequestDigest == digest
                    ? ToResult(existing, replayed: true)
                    : Unsaved(challengeId, "CHALLENGE_CONFLICT", "challenge_id was already used with a different body.");

            var failure = ValidateEnvelope(root, out var eventsElement);
            if (failure is not null)
                return await PersistAsync(challengeId, digest, payload, received, MqttStatuses.Failed,
                    FailedJson(challengeId, failure.Value.Code, failure.Value.Message), failure.Value.Code, failure.Value.Message, ct);

            var items = eventsElement.EnumerateArray().ToList();
            var batch = await eventService.ProcessBatchAsync(items, Channels.Mqtt, ct);
            var state = await stateService.GetSummaryAsync(null, ct);

            var response = new MqttResponse(ProtocolVersion, options.Value.CandidateId, challengeId, MqttStatuses.Completed,
                Format(clock.UtcNow), batch.Results, state, null, null);
            return await PersistAsync(challengeId, digest, payload, received, MqttStatuses.Completed,
                JsonSerializer.Serialize(response, JsonDefaults.Mqtt), null, null, ct);
        }
    }

    private (string Code, string Message)? ValidateEnvelope(JsonElement root, out JsonElement eventsElement)
    {
        eventsElement = default;

        if (ReadString(root, "protocol_version") != ProtocolVersion)
            return ("UNSUPPORTED_PROTOCOL", $"Only protocol_version {ProtocolVersion} is supported.");

        if (!string.Equals(ReadString(root, "candidate_id"), options.Value.CandidateId, StringComparison.Ordinal))
            return ("CANDIDATE_MISMATCH", "candidate_id does not match this device.");

        if (ReadString(root, "command") != Command)
            return ("VALIDATION_ERROR", $"command must be {Command}.");

        var expiresText = ReadString(root, "expires_at");
        if (expiresText is null || !DateTimeOffset.TryParse(expiresText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expires))
            return ("VALIDATION_ERROR", "expires_at must be an ISO 8601 timestamp with timezone.");
        if (expires <= clock.UtcNow)
            return ("CHALLENGE_EXPIRED", "The challenge has expired.");

        if (!root.TryGetProperty("events", out eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
            return ("VALIDATION_ERROR", "events must be an array.");

        return null;
    }

    private async Task<MqttHandlerResult> PersistAsync(
        string challengeId, string digest, string body, DateTimeOffset received, string status,
        string responseJson, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        var entity = new MqttChallenge
        {
            ChallengeId = challengeId,
            RequestDigest = digest,
            RequestBody = body,
            ResponseBody = responseJson,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ReceivedAt = received,
            RespondedAt = clock.UtcNow
        };

        if (!await challenges.TryAddAsync(entity, ct))
        {
            // Someone stored this challenge_id first: answer with what was stored.
            var stored = await challenges.FindAsync(challengeId, ct);
            if (stored is not null)
                return stored.RequestDigest == digest
                    ? ToResult(stored, replayed: true)
                    : Unsaved(challengeId, "CHALLENGE_CONFLICT", "challenge_id was already used with a different body.");
        }

        if (status == MqttStatuses.Failed)
            tracker.SetError($"{errorCode}: {errorMessage}", clock.UtcNow);

        return new MqttHandlerResult(challengeId, status, errorCode, responseJson, false);
    }

    private MqttHandlerResult Unsaved(string? challengeId, string code, string message)
    {
        tracker.SetError($"{code}: {message}", clock.UtcNow);
        return new MqttHandlerResult(challengeId, MqttStatuses.Failed, code, FailedJson(challengeId, code, message), false);
    }

    private string FailedJson(string? challengeId, string code, string message) =>
        JsonSerializer.Serialize(
            new MqttResponse(ProtocolVersion, options.Value.CandidateId, challengeId, MqttStatuses.Failed,
                Format(clock.UtcNow), null, null, code, message),
            JsonDefaults.Mqtt);

    private static MqttHandlerResult ToResult(MqttChallenge stored, bool replayed) =>
        new(stored.ChallengeId, stored.Status, stored.ErrorCode, stored.ResponseBody, replayed);

    private static string Format(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? TryReadChallengeId(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? ReadString(doc.RootElement, "challenge_id") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Whitespace-insensitive hash of the whole challenge body.</summary>
    private static string ComputeDigest(string payload)
    {
        var canonical = JsonNode.Parse(payload)!.ToJsonString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}