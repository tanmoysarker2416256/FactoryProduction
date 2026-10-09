namespace FactoryProduction.Application;

/// <summary>state module: read-only totals and lists. REST and MQTT use these same functions.</summary>
public sealed class StateService(IStateQueries queries)
{
    public const string Summary = "summary";
    public const string Pending = "pending";
    public const string Exceptions = "exceptions";

    public static bool IsValidView(string? view) => view is Summary or Pending or Exceptions;

    /// <summary>get_summary(): the six dashboard numbers, read from the database every time.</summary>
    public Task<StateSummary> GetSummaryAsync(string? sourceId, CancellationToken ct) =>
        queries.GetSummaryAsync(Clean(sourceId), ct);

    public async Task<ItemsResponse<PendingItem>> GetPendingAsync(string? sourceId, CancellationToken ct)
    {
        var items = await queries.GetPendingAsync(Clean(sourceId), ct);
        return new ItemsResponse<PendingItem>(Pending, items.Count, items);
    }

    public async Task<ItemsResponse<ExceptionItem>> GetExceptionsAsync(string? sourceId, CancellationToken ct)
    {
        var items = await queries.GetExceptionsAsync(Clean(sourceId), ct);
        return new ItemsResponse<ExceptionItem>(Exceptions, items.Count, items);
    }

    private static string? Clean(string? sourceId) => string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();
}
