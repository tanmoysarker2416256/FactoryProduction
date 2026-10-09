namespace FactoryProduction.Application;

public sealed class MqttOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "152.42.238.142";
    public int Port { get; set; } = 1883;
    public string CandidateId { get; set; } = string.Empty;
    public int HeartbeatSeconds { get; set; } = 20;

    public string ChallengeTopic => $"fse-01/{CandidateId}/challenge";
    public string ResponseTopic => $"fse-01/{CandidateId}/response";
    public string StatusTopic => $"fse-01/{CandidateId}/status";
}
