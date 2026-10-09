using FactoryProduction.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FactoryProduction.Infrastructure;

public static class DependencyInjection
{
    /// <summary>SQL Server persistence: DbContext, unit of work and every module's repository.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IAckRepository, AckRepository>();
        services.AddScoped<IStateQueries, StateQueries>();
        services.AddScoped<IMqttChallengeRepository, MqttChallengeRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }

    /// <summary>Binds the "Mqtt" settings and starts the background MQTT client.</summary>
    public static IServiceCollection AddMqttWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MqttOptions>(configuration.GetSection("Mqtt"));
        services.AddHostedService<MqttWorker>();
        return services;
    }
}
