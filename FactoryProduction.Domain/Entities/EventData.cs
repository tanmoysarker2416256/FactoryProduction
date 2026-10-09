namespace FactoryProduction.Domain.Entities;

/// <summary>Validated, normalized event values (the output of validate_event).</summary>
public sealed record EventData(
    string SourceId,
    string EventId,
    string Type,
    int? Quantity,
    string? TargetEventId,
    DateTimeOffset EventTime);
