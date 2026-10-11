using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Exuarch.Web.Components
{
    public sealed class KeyedHandlers<TKey, TArgs>
    {
        private readonly Dictionary<TKey, Func<TArgs, Task>> handlers = new Dictionary<TKey, Func<TArgs, Task>>();
        private readonly Func<TKey, TArgs, Task> handle;

        public KeyedHandlers(Func<TKey, TArgs, Task> handle)
        {
            this.handle = handle;
        }

        public KeyedHandlers(Action<TKey> handle) : this((key, _) =>
        {
            handle(key);
            return Task.CompletedTask;
        })
        {
        }

        public Func<TArgs, Task> For(TKey key)
        {
            if (!handlers.TryGetValue(key, out var handler)) handlers[key] = handler = args => handle(key, args);
            return handler;
        }
    }
}
