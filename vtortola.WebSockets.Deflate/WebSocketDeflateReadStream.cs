using System;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip.Compression;
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
        private readonly BufferManager bufferManager;
        private readonly Inflater inflater;
        private readonly byte[] inflaterBuffer;
        private volatile int state = STATE_OPEN;

        public override WebSocketMessageType MessageType => this.innerStream.MessageType;
        public override WebSocketExtensionFlags Flags => this.innerStream.Flags;

        /// <inheritdoc />
        internal override WebSocketListenerOptions Options => this.innerStream.Options;

        public WebSocketDeflateReadStream([NotNull] WebSocketMessageReadStream innerStream)
        {
            if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));

            this.innerStream = innerStream;
            this.bufferManager = innerStream.Options.BufferManager;
            this.inflaterBuffer = this.bufferManager.TakeBuffer(this.bufferManager.LargeBufferSize);
            this.inflater = new Inflater(noHeader: false);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count <= 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (this.inflater.IsFinished)
            {
                return 0;
            }

            var totalBytesInflated = 0;
            while (this.inflater.IsNeedingInput && count > 0)
            {
                var read = await this.innerStream.ReadAsync(this.inflaterBuffer, 0, this.inflaterBuffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return 0;
                }
                this.inflater.SetInput(this.inflaterBuffer, 0, read);

                var inflatedBytes = 0;
                do
                {
                    inflatedBytes = this.inflater.Inflate(buffer, offset, count);
                    offset += inflatedBytes;
                    count -= inflatedBytes;
                    totalBytesInflated += inflatedBytes;
                } while (!this.inflater.IsFinished && inflatedBytes > 0 && count > 0);
            }

            return totalBytesInflated;
        }

        public override Task CloseAsync()
        {
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
            {
                return TaskHelper.CompletedTask;
            }

            return this.innerStream.CloseAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref this.state, STATE_DISPOSED) == STATE_DISPOSED)
            {
                return;
            }

            SafeEnd.Dispose(this.innerStream);

            this.bufferManager.ReturnBuffer(this.inflaterBuffer);
        }
    }
}
