using FactoryProduction.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FactoryProduction.Application;

/// <summary>
/// In-process notifications. A failing handler never undoes committed business data.
/// To move to a broker later, replace this class with one that publishes to Kafka/RabbitMQ.
/// </summary>
public sealed class InProcessDomainEventPublisher(
    IEnumerable<IDomainEventHandler> handlers,
    ILogger<InProcessDomainEventPublisher> logger) : IDomainEventPublisher
{
    public async Task PublishAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(domainEvent, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Handler {Handler} failed for {Event}", handler.GetType().Name, domainEvent.Name);
                }
            }
        }
    }
}
