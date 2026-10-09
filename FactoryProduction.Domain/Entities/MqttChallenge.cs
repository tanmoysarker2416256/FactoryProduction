namespace FactoryProduction.Domain.Entities;

/// <summary>A device-simulator request (by challenge_id) and the response we published, kept for replay safety.</summary>
public sealed class MqttChallenge
{
    public string ChallengeId { get; set; } = string.Empty;
    public string RequestDigest { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public string ResponseBody { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset RespondedAt { get; set; }
}
