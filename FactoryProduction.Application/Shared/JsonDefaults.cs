using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryProduction.Application;

public static class JsonDefaults
{
    /// <summary>snake_case, nulls are written (REST + stored payload snapshots).</summary>
    public static readonly JsonSerializerOptions Rest = Create(ignoreNulls: false);

    /// <summary>snake_case, nulls omitted (MQTT responses).</summary>
    public static readonly JsonSerializerOptions Mqtt = Create(ignoreNulls: true);

    private static JsonSerializerOptions Create(bool ignoreNulls)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        if (ignoreNulls) options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
