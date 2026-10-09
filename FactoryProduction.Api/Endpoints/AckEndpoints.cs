using System.Text.Json;
using FactoryProduction.Application;

namespace FactoryProduction.Api;

public static class AckEndpoints
{
    /// <summary>POST /api/ack  { "event_ids": ["EV-101", "EV-102"] }</summary>
    public static void MapAckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ack", async (HttpRequest request, AckService service, CancellationToken ct) =>
        {
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new ErrorResponse("invalid_json", "Request body must be JSON."));
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("event_ids", out var idsElement)
                    || idsElement.ValueKind != JsonValueKind.Array
                    || idsElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                {
                    return Results.BadRequest(new ErrorResponse("invalid_body", "Body must be {\"event_ids\": [\"EV-1\", ...]} with string IDs."));
                }

                var ids = idsElement.EnumerateArray().Select(e => e.GetString()!).ToList();
                return Results.Ok(await service.AcknowledgeEventsAsync(ids, ct));
            }
        });
    }
}
