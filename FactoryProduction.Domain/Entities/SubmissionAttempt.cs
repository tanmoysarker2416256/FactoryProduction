using System.Threading.Channels;

namespace FactoryProduction.Domain.Entities;

/// <summary>Every received item - accepted, duplicate, conflicting or rejected - is kept here.</summary>
public sealed class SubmissionAttempt
{
    public long Id { get; set; }
    public string Channel { get; set; } = Channels.Rest;
    public string RawPayload { get; set; } = string.Empty;
    public string? NormalizedData { get; set; }
    public string? SourceId { get; set; }
    public string? EventId { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
