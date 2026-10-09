namespace FactoryProduction.Domain.Entities;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public string? SourceId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
