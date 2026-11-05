using Microsoft.Extensions.DependencyInjection;

namespace HomeShowing.BuildingBlocks.Events;

public interface IDomainEvent
{
    string TenantId { get; }
}

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventDispatcher
{
    /// <summary>Dispatches to the handlers of the event's runtime type (so an event held as <see cref="IDomainEvent"/> still reaches them).</summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct);
}

/// <summary>
/// In process, synchronous dispatcher (no MediatR). Handlers run inside the caller's scope and,
/// when the caller opened a transaction, inside that transaction: a handler that writes an outbox
/// row commits or rolls back together with the state change that raised the event.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        foreach (var handler in services.GetServices(handlerType))
            await (Task)handle.Invoke(handler!, [domainEvent, ct])!;
    }
}
