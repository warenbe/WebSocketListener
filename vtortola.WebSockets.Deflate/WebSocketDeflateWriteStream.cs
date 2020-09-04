using System;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace vtortola.WebSockets.Deflate
{
    public sealed class WebSocketDeflateWriteStream : WebSocketMessageWriteStream
    {
        private static readonly byte[] FINAL_BYTE = new byte[] { 0 };

        private const int STATE_OPEN = 0;
        private const int STATE_CLOSED = 1;
        private const int STATE_DISPOSED = 2;

        private readonly WebSocketMessageWriteStream innerStream;
        private readonly DeflateStream deflateStream;
        private volatile int state = STATE_OPEN;

        /// <inheritdoc />
        internal override WebSocketListenerOptions Options => this.innerStream.Options;

        public WebSocketDeflateWriteStream([NotNull]WebSocketMessageWriteStream innerStream)
        {
            if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));

            this.innerStream = innerStream;
            this.deflateStream = new DeflateStream(innerStream, CompressionLevel.Optimal, leaveOpen: true);
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
            {
                return Task.FromResult(0);
            }

            return this.deflateStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override async Task WriteAndCloseAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count > 0)
            {
                await this.deflateStream.WriteAsync(buffer, offset, count, cancellationToken);
            }

            await this.CloseAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task CloseAsync()
        {
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
                return;

            // flush remaining data
            await this.deflateStream.FlushAsync(CancellationToken.None);
            this.deflateStream.Dispose(); // will flush deflate data
            // close inner stream
            await this.innerStream.WriteAndCloseAsync(FINAL_BYTE, 0, 1, CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref this.state, STATE_DISPOSED) == STATE_DISPOSED)
                return;

            SafeEnd.Dispose(this.deflateStream);
            SafeEnd.Dispose(this.innerStream);
            base.Dispose(disposing);
        }

    }
}
