using System.Collections.Concurrent;

namespace AIToolkit.Agent
{
    public static class EventBus
    {
        private static readonly ConcurrentDictionary<Type, List<Delegate>> _subs = new();

        public static void Publish<T>(T agentevent)
        {
            if (_subs.TryGetValue(typeof(T), out var handlers))
            {
                foreach (var h in handlers.ToArray())
                {
                    try { ((Action<T>)h)(agentevent); } catch 
                    { 
                        // swallow dat exception, for now, we'll spit out some log later.
                    }
                }
            }
        }

        public static void Subscribe<T>(Action<T> handler)
        {
            var list = _subs.GetOrAdd(typeof(T), _ => []);
            lock (list)
            {
                if (!list.Contains(handler))
                    list.Add(handler);
            }
        }

        public static void UnsubscribeAll() => _subs.Clear();
    }

    // Core events
    public record MessageAddedEvent(Guid MessageId, bool IsUser, DateTime Date);
    public record SessionArchivedEvent(Guid SessionId, DateTime When);
    public record PersonaUpdatedEvent(string PersonaId, DateTime When);
    public record BotChangedEvent(string BotId);
    public record StagedMessageReadyEvent(StagedMessage Message);
}