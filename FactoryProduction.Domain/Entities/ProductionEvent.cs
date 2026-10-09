namespace FactoryProduction.Domain.Entities;

/// <summary>
/// One logical production event (a COUNT, or a VOID correction). Status is one of
/// ACCEPTED, PENDING_REFERENCE (VOID waiting for its COUNT) or REJECTED (a pending VOID that lost).
/// History is never deleted: corrections are new rows, not edits to the COUNT.
/// </summary>
public sealed class ProductionEvent
{
    private ProductionEvent()
    {
        EventId = SourceId = Type = Status = string.Empty;
    }

    public long Id { get; private set; }
    public string EventId { get; private set; }
    public string SourceId { get; private set; }
    public string Type { get; private set; }
    public int? Quantity { get; private set; }
    public string? TargetEventId { get; private set; }
    public DateTimeOffset EventTime { get; private set; }
    public string Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public static ProductionEvent Create(EventData data, string status, DateTimeOffset receivedAt, string? reason = null) => new()
    {
        EventId = data.EventId,
        SourceId = data.SourceId,
        Type = data.Type,
        Quantity = data.Quantity,
        TargetEventId = data.TargetEventId,
        EventTime = data.EventTime,
        Status = status,
        FailureReason = reason,
        ReceivedAt = receivedAt
    };

    /// <summary>True when a re-submission carries exactly the same normalized values.</summary>
    public bool Matches(EventData data) =>
        string.Equals(SourceId, data.SourceId, StringComparison.OrdinalIgnoreCase)
        && Type == data.Type
        && Quantity == data.Quantity
        && string.Equals(TargetEventId, data.TargetEventId, StringComparison.OrdinalIgnoreCase)
        && EventTime.UtcDateTime == data.EventTime.UtcDateTime;

    /// <summary>VOIDs are correction records: they are acknowledged automatically when they complete.</summary>
    public void AutoAcknowledge(DateTimeOffset now) => AcknowledgedAt = now;

    /// <summary>A pending VOID found its COUNT and won.</summary>
    public void ResolveAsAccepted(DateTimeOffset now)
    {
        Status = ItemStatuses.Accepted;
        FailureReason = null;
        ResolvedAt = now;
        AutoAcknowledge(now);
    }

    /// <summary>A pending VOID lost (another VOID won, or the COUNT belongs to another source).</summary>
    public void Reject(string reason, DateTimeOffset now)
    {
        Status = ItemStatuses.Rejected;
        FailureReason = reason;
        ResolvedAt = now;
    }
}
