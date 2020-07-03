using System;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using vtortola.WebSockets.Tools;

namespace vtortola.WebSockets.Deflate
{
    public sealed class WebSocketDeflateReadStream : WebSocketMessageReadStream
    {
        private const int STATE_OPEN = 0;
        private const int STATE_CLOSED = 1;
        private const int STATE_DISPOSED = 2;

        private readonly WebSocketMessageReadStream innerStream;
        readonly DeflateStream deflateStream;
        private volatile int state = STATE_OPEN;

        public override WebSocketMessageType MessageType => this.innerStream.MessageType;
        public override WebSocketExtensionFlags Flags => this.innerStream.Flags;

        /// <inheritdoc />
        internal override WebSocketListenerOptions Options => this.innerStream.Options;

        public WebSocketDeflateReadStream([NotNull] WebSocketMessageReadStream innerStream)
        {
            if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));

            this.innerStream = innerStream;
            this.deflateStream = new DeflateStream(innerStream, CompressionMode.Decompress, leaveOpen: true);
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.deflateStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override Task CloseAsync()
        {
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
            {
                return TaskHelper.CompletedTask;
            }

            return this.innerStream.CloseAsync();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref this.state, STATE_DISPOSED) == STATE_DISPOSED)
            {
                return;
            }

            SafeEnd.Dispose(this.deflateStream);
            SafeEnd.Dispose(this.innerStream);
        }
    }
}
