using System;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip.Compression;
using JetBrains.Annotations;

namespace vtortola.WebSockets.Deflate
{
    public sealed class WebSocketDeflateWriteStream : WebSocketMessageWriteStream
    {
        private const int STATE_OPEN = 0;
        private const int STATE_CLOSED = 1;
        private const int STATE_DISPOSED = 2;

        private readonly WebSocketMessageWriteStream innerStream;
        private readonly BufferManager bufferManager;
        private readonly Deflater deflater;
        private readonly byte[] deflaterBuffer;
        private volatile int state = STATE_OPEN;

        /// <inheritdoc />
        internal override WebSocketListenerOptions Options => this.innerStream.Options;

        public WebSocketDeflateWriteStream([NotNull]WebSocketMessageWriteStream innerStream)
        {
            if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));

            this.innerStream = innerStream;
            this.bufferManager = innerStream.Options.BufferManager;
            this.deflaterBuffer = this.bufferManager.TakeBuffer(this.bufferManager.LargeBufferSize);
            this.deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, noZlibHeaderOrFooter: false);
        }

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return;

            this.deflater.SetInput(buffer, offset, count);
            while (this.deflater.IsNeedingInput == false)
            {
                var deflatedBytes = this.deflater.Deflate(this.deflaterBuffer, 0, this.deflaterBuffer.Length);
                if (deflatedBytes <= 0)
                    break;

                await this.innerStream.WriteAsync(this.deflaterBuffer, 0, deflatedBytes, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override async Task WriteAndCloseAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count > 0)
            {
                await this.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }

            await this.CloseAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task CloseAsync()
        {
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
                return;

            // flush remaining data
            this.deflater.Finish();
            while (this.deflater.IsFinished == false)
            {
                var deflatedBytes = this.deflater.Deflate(this.deflaterBuffer, 0, this.deflaterBuffer.Length);
                if (this.deflater.IsFinished)
                {
                    await this.innerStream.WriteAndCloseAsync(this.deflaterBuffer, 0, deflatedBytes, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await this.innerStream.WriteAsync(this.deflaterBuffer, 0, deflatedBytes).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref this.state, STATE_DISPOSED) == STATE_DISPOSED)
                return;

            SafeEnd.Dispose(this.innerStream);
            base.Dispose(disposing);

            this.bufferManager.ReturnBuffer(this.deflaterBuffer);
        }

    }
}
