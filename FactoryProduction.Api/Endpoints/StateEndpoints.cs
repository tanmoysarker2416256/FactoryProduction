using FactoryProduction.Application;

namespace FactoryProduction.Api;

public static class StateEndpoints
{
    /// <summary>GET /api/state?source_id=LINE-01&amp;view=summary|pending|exceptions</summary>
    public static void MapStateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/state", async (string? source_id, string? view, StateService service, CancellationToken ct) =>
        {
            var selected = string.IsNullOrWhiteSpace(view) ? StateService.Summary : view.Trim();
            if (!StateService.IsValidView(selected))
                return Results.BadRequest(new ErrorResponse("invalid_view", "view must be one of: summary, pending, exceptions."));

            return selected switch
            {
                StateService.Pending => Results.Ok(await service.GetPendingAsync(source_id, ct)),
                StateService.Exceptions => Results.Ok(await service.GetExceptionsAsync(source_id, ct)),
                _ => Results.Ok(await service.GetSummaryAsync(source_id, ct))
            };
        });
    }
}
