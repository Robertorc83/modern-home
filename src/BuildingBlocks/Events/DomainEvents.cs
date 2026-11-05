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
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent;
}

/// <summary>
/// In process, synchronous dispatcher (no MediatR). Handlers run inside the caller's scope and,
/// when the caller opened a transaction, inside that transaction: a handler that writes an outbox
/// row commits or rolls back together with the state change that raised the event.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, ct);
    }
}
