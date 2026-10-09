using System.Text;
using FactoryProduction.Application;

namespace FactoryProduction.Api;

public static class MqttEndpoints
{
    public static void MapMqttEndpoints(this IEndpointRouteBuilder app, bool isDevelopment)
    {
        // Used by the dashboard's MQTT card.
        app.MapGet("/api/mqtt/status", async (MqttStatusService service, CancellationToken ct) =>
            Results.Ok(await service.GetStatusAsync(ct)));

        // Development only: feed a challenge to the SAME handler the MQTT worker uses, without a broker.
        if (isDevelopment)
        {
            app.MapPost("/api/dev/mqtt-challenge", async (HttpRequest request, MqttChallengeHandler handler, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(ct);
                var result = await handler.HandleMqttChallengeAsync(body, ct);
                return Results.Content(result.ResponseJson, "application/json");
            });
        }
    }
}
