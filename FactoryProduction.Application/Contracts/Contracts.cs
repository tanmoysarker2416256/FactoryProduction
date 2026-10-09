namespace FactoryProduction.Application;

// Data contracts. Property names are written as snake_case JSON (see JsonDefaults).
public sealed record EventResult(string? EventId, string Status, string Message);
public sealed record ProcessEventsResponse(IReadOnlyList<EventResult> Results);

public sealed record AckResult(string EventId, string Status);
public sealed record AckResponse(IReadOnlyList<AckResult> Results);

public sealed record StateSummary(long NetTotal, int ProcessedEvents, int PendingAck, int Unresolved, int Duplicates, int Conflicts);

public sealed record PendingItem(
    string EventId, string SourceId, string Type, int? Quantity, string? TargetEventId,
    DateTimeOffset EventTime, DateTimeOffset ReceivedAt, string Status, bool Voided);

public sealed record ExceptionItem(
    string Kind, string? EventId, string? SourceId, string Status, string Reason,
    DateTimeOffset OccurredAt, string? Payload);

public sealed record ItemsResponse<T>(string View, int Count, IReadOnlyList<T> Items);

public sealed record ErrorResponse(string Error, string Message);

public sealed record MqttStatusResponse(
    bool Enabled, bool Connected, string Broker, string CandidateId,
    string ChallengeTopic, string ResponseTopic, string StatusTopic,
    string? LastChallengeId, DateTimeOffset? LastChallengeAt, string? LastResponseStatus,
    int ChallengesTotal, int ChallengesCompleted, int ChallengesFailed,
    string? LastError, DateTimeOffset? LastErrorAt, DateTimeOffset? LastHeartbeatAt);

public sealed record MqttResponse(
    string ProtocolVersion, string CandidateId, string? ChallengeId, string Status, string ProcessedAt,
    IReadOnlyList<EventResult>? Results, StateSummary? State, string? ErrorCode, string? Message);
