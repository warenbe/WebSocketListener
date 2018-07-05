/*
	Copyright (c) 2017 Denis Zykov
	License: https://opensource.org/licenses/MIT
*/
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace vtortola.WebSockets.Transports
{
    public abstract class WebSocketTransport
    {
        public abstract IReadOnlyCollection<string> Schemes { get; }

        internal abstract Task<Listener> ListenAsync(Uri address, WebSocketListenerOptions options);
        internal abstract Task<NetworkConnection> ConnectAsync(Uri address, WebSocketListenerOptions options, CancellationToken cancellation);
        internal abstract bool ShouldUseSsl(Uri requestUri);

        /// <inheritdoc />
        public virtual WebSocketTransport Clone()
        {
            return (WebSocketTransport)this.MemberwiseClone();
        }        
    }
}
