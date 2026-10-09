namespace FactoryProduction.Domain.Entities;

/// <summary>A line or machine that sends counts, e.g. LINE-01.</summary>
public sealed class ProductionSource
{
    public ProductionSource(string sourceId, string displayName, DateTimeOffset createdAt)
    {
        SourceId = sourceId;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    public string SourceId { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
