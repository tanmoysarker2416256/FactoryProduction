namespace FactoryProduction.Domain;

public static class EventTypes
{
    public const string Count = "COUNT";
    public const string Void = "VOID";
}

/// <summary>Statuses used both for submission results and for stored logical events.</summary>
public static class ItemStatuses
{
    public const string Accepted = "ACCEPTED";
    public const string Duplicate = "DUPLICATE";
    public const string Conflict = "CONFLICT";
    public const string PendingReference = "PENDING_REFERENCE";
    public const string Rejected = "REJECTED";
}

public static class AckStatuses
{
    public const string Acked = "ACKED";
    public const string AlreadyAcked = "ALREADY_ACKED";
    public const string NotReady = "NOT_READY";
    public const string NotFound = "NOT_FOUND";
}

public static class Channels
{
    public const string Rest = "REST";
    public const string Mqtt = "MQTT";
}

public static class MqttStatuses
{
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
