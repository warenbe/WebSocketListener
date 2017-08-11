/*
	Copyright (c) 2017 Denis Zykov
	License: https://opensource.org/licenses/MIT
*/
using System.Collections.Concurrent;
using System.Threading;

#pragma warning disable 420

namespace vtortola.WebSockets.Async
{
    internal sealed class AsyncQueue<ItemT> : AsyncCollection<ItemT, ConcurrentQueue<ItemT>>
    {
        /// <inheritdoc />
        public AsyncQueue(int boundedCapacity = UNBOUND)
            : base(new ConcurrentQueue<ItemT>(), q => q.IsEmpty, boundedCapacity)
        {

        }

        public TakeResult DequeueAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.TakeAsync(cancellationToken);
        }
        public bool TryDequeue(out ItemT value)
        {
            return this.TryTake(out value);
        }
        public bool TryEnqueue(ItemT item)
        {
            return this.TryAdd(item);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"AsyncQueue, count: {this.Count}";
        }
    }
}
