using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FactoryProduction.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IMqttStatusTracker, MqttStatusTracker>();

        services.AddScoped<IDomainEventPublisher, InProcessDomainEventPublisher>();
        services.AddScoped<IDomainEventHandler, AuditEventHandler>();

        services.AddScoped<EventProcessingService>();
        services.AddScoped<AckService>();
        services.AddScoped<StateService>();
        services.AddScoped<MqttChallengeHandler>();
        services.AddScoped<MqttStatusService>();
        return services;
    }
}
