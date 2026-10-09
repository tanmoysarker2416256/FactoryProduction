namespace FactoryProduction.Application;

public sealed record MqttConnectionSnapshot(bool Connected, DateTimeOffset? LastHeartbeatAt, string? LastError, DateTimeOffset? LastErrorAt);

/// <summary>In-memory connection facts (durable challenge history comes from the database).</summary>
public interface IMqttStatusTracker
{
    void SetConnected(bool connected);
    void SetHeartbeat(DateTimeOffset at);
    void SetError(string message, DateTimeOffset at);
    MqttConnectionSnapshot Snapshot();
}

public sealed class MqttStatusTracker : IMqttStatusTracker
{
    private readonly object _gate = new();
    private bool _connected;
    private DateTimeOffset? _heartbeat;
    private string? _error;
    private DateTimeOffset? _errorAt;

    public void SetConnected(bool connected) { lock (_gate) _connected = connected; }
    public void SetHeartbeat(DateTimeOffset at) { lock (_gate) _heartbeat = at; }
    public void SetError(string message, DateTimeOffset at) { lock (_gate) { _error = message; _errorAt = at; } }

    public MqttConnectionSnapshot Snapshot()
    {
        lock (_gate) return new MqttConnectionSnapshot(_connected, _heartbeat, _error, _errorAt);
    }
}
