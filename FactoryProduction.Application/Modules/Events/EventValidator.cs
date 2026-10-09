using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FactoryProduction.Domain;
using FactoryProduction.Domain.Entities;

namespace FactoryProduction.Application;

public sealed record ValidationOutcome(EventData? Data, string? SourceId, string? EventId, IReadOnlyList<string> Errors)
{
    public bool IsValid => Data is not null;
    public string ErrorText => string.Join("; ", Errors);
}

/// <summary>validate_event(): pure function, no database. Turns one JSON item into normalized EventData or a list of reasons.</summary>
public static partial class EventValidator
{
    public const int MaxIdLength = 100;

    // ISO 8601 date-time that MUST carry a timezone (Z or +hh:mm).
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex IsoWithZone();

    public static ValidationOutcome ValidateEvent(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return new ValidationOutcome(null, null, null, new[] { "Each event must be a JSON object" });

        var errors = new List<string>();
        var sourceId = ReadRequiredString(item, "source_id", errors);
        var eventId = ReadRequiredString(item, "event_id", errors);

        string? type = null;
        if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind == JsonValueKind.Null)
            errors.Add("type is required");
        else if (typeEl.ValueKind != JsonValueKind.String
                 || (typeEl.GetString() != EventTypes.Count && typeEl.GetString() != EventTypes.Void))
            errors.Add("type must be exactly COUNT or VOID");
        else
            type = typeEl.GetString();

        int? quantity = null;
        string? target = null;
        var hasQuantity = item.TryGetProperty("quantity", out var q) && q.ValueKind != JsonValueKind.Null;
        var hasTarget = item.TryGetProperty("target_event_id", out var t) && t.ValueKind != JsonValueKind.Null;

        if (type == EventTypes.Count)
        {
            if (!hasQuantity) errors.Add("quantity is required for COUNT");
            else if (q.ValueKind != JsonValueKind.Number || !q.TryGetInt32(out var qi) || qi <= 0)
                errors.Add("quantity must be a positive integer");
            else quantity = qi;

            if (hasTarget) errors.Add("target_event_id must be null or omitted for COUNT");
        }
        else if (type == EventTypes.Void)
        {
            if (hasQuantity) errors.Add("quantity must be null or omitted for VOID");
            target = ReadRequiredString(item, "target_event_id", errors);
            if (target is not null && eventId is not null && string.Equals(target, eventId, StringComparison.OrdinalIgnoreCase))
                errors.Add("a VOID cannot target itself");
        }

        DateTimeOffset eventTime = default;
        var timeOk = false;
        if (!item.TryGetProperty("event_time", out var timeEl) || timeEl.ValueKind == JsonValueKind.Null)
            errors.Add("event_time is required");
        else if (timeEl.ValueKind != JsonValueKind.String)
            errors.Add("event_time must be an ISO 8601 string with timezone");
        else
        {
            var text = timeEl.GetString()!.Trim();
            if (IsoWithZone().IsMatch(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out eventTime))
                timeOk = true;
            else
                errors.Add("event_time must be an ISO 8601 timestamp with timezone, e.g. 2026-10-09T10:30:00Z");
        }

        if (errors.Count > 0)
            return new ValidationOutcome(null, sourceId, eventId, errors);

        return new ValidationOutcome(
            new EventData(sourceId!, eventId!, type!, quantity, target, eventTime),
            sourceId, eventId, Array.Empty<string>());
    }

    private static string? ReadRequiredString(JsonElement obj, string name, List<string> errors)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{name} is required");
            return null;
        }
        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{name} must be a string");
            return null;
        }
        var value = el.GetString()!.Trim();
        if (value.Length == 0)
        {
            errors.Add($"{name} must not be empty");
            return null;
        }
        if (value.Length > MaxIdLength)
        {
            errors.Add($"{name} must be at most {MaxIdLength} characters");
            return null;
        }
        return value;
    }
}
