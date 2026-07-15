using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HonzaBotner.Discord.EventHandler;

internal class EventHandler
{
    private readonly IServiceProvider _provider;
    private readonly OrderedEventHandlersList _eventHandlers;
    private readonly ILogger<EventHandler> _logger;

    public EventHandler(IServiceProvider provider, OrderedEventHandlersList eventHandlers,
        ILogger<EventHandler> logger)
    {
        _provider = provider;
        _eventHandlers = eventHandlers;
        _logger = logger;
    }

    public async Task Handle<T>(T eventArgs) where T : EventArgs
    {
        using IServiceScope scope = _provider.CreateScope();

        foreach (Type reactionHandlerType in _eventHandlers)
        {
            if (!reactionHandlerType.IsAssignableTo(typeof(IEventHandler<T>)))
            {
                continue;
            }

            IEventHandler<T> handler =
                scope.ServiceProvider.GetService(reactionHandlerType) as IEventHandler<T>
                ?? throw new ArgumentOutOfRangeException();
            EventHandlerResult shouldStop;
            try
            {
                shouldStop = await handler.Handle(eventArgs).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Discord event handler {HandlerType} failed", reactionHandlerType.Name);
                continue;
            }
            if (shouldStop == EventHandlerResult.Stop) return;
        }
    }
}
