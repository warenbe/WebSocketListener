using System;
using System.Threading;
using System.Threading.Tasks;
using vtortola.WebSockets.Tools;

#pragma warning disable 420

namespace vtortola.WebSockets.Rfc6455
{
    internal class WebSocketMessageWriteRfc6455Stream : WebSocketMessageWriteStream
    {
        private const int STATE_OPEN = 0;
        private const int STATE_CLOSED = 1;
        private const int STATE_DISPOSED = 2;

        private readonly ArraySegment<byte> sendBuffer;
        private bool isHeaderSent;
        private int sendBufferUsedLength;
        private volatile int state = STATE_OPEN;

        private readonly WebSocketRfc6455 webSocket;
        private readonly WebSocketMessageType messageType;

        /// <inheritdoc />
        internal override WebSocketListenerOptions Options => this.webSocket.Connection.Options;

        public WebSocketMessageWriteRfc6455Stream(WebSocketRfc6455 webSocket, WebSocketMessageType messageType)
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));

            this.sendBufferUsedLength = 0;
            this.messageType = messageType;
            this.webSocket = webSocket;
            this.sendBuffer = this.webSocket.Connection.SendBuffer;
        }
        public WebSocketMessageWriteRfc6455Stream(WebSocketRfc6455 webSocket, WebSocketMessageType messageType, WebSocketExtensionFlags extensionFlags)
            : this(webSocket, messageType)
        {
            this.ExtensionFlags.Rsv1 = extensionFlags.Rsv1;
            this.ExtensionFlags.Rsv2 = extensionFlags.Rsv2;
            this.ExtensionFlags.Rsv3 = extensionFlags.Rsv3;
            this.sendBuffer = this.webSocket.Connection.SendBuffer;
        }

        private void BufferData(byte[] buffer, ref int offset, ref int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            var read = Math.Min(count, this.sendBuffer.Count - this.sendBufferUsedLength);
            if (read == 0)
                return;

            Array.Copy(buffer, offset, this.sendBuffer.Array, this.sendBuffer.Offset + this.sendBufferUsedLength, read);
            this.sendBufferUsedLength += read;
            offset += read;
            count -= read;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            this.ThrowIfDisposed();

            while (count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                this.BufferData(buffer, ref offset, ref count);

                if (this.sendBufferUsedLength == this.sendBuffer.Count && count > 0)
                {
                    var dataFrame = this.webSocket.Connection.PrepareFrame(this.sendBuffer, this.sendBufferUsedLength, false, this.isHeaderSent, this.messageType, this.ExtensionFlags);
                    await this.webSocket.Connection.SendFrameAsync(dataFrame, cancellationToken).ConfigureAwait(false);
                    this.sendBufferUsedLength = 0;
                    this.isHeaderSent = true;
                }
            }
        }

        /// <inheritdoc />
        public override async Task WriteAndCloseAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
                return;

            var bytesToSend = count + this.sendBufferUsedLength;
            var isLastFrame = false;
            do
            {
                this.BufferData(buffer, ref offset, ref count);

                isLastFrame = bytesToSend <= this.sendBufferUsedLength;
                var dataFrame = this.webSocket.Connection.PrepareFrame(this.sendBuffer, this.sendBufferUsedLength, isLastFrame, this.isHeaderSent, this.messageType, this.ExtensionFlags);
                await this.webSocket.Connection.SendFrameAsync(dataFrame, cancellationToken).ConfigureAwait(false);
                bytesToSend -= this.sendBufferUsedLength;
                this.sendBufferUsedLength = 0;
                this.isHeaderSent = true;

            } while (isLastFrame == false);

            this.webSocket.Connection.EndWriting();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            if (this.sendBufferUsedLength <= 0 && this.isHeaderSent)
                return;

            var dataFrame = this.webSocket.Connection.PrepareFrame(this.sendBuffer, this.sendBufferUsedLength, false, this.isHeaderSent, this.messageType, this.ExtensionFlags);
            await this.webSocket.Connection.SendFrameAsync(dataFrame, cancellationToken).ConfigureAwait(false);
            this.sendBufferUsedLength = 0;
            this.isHeaderSent = true;
        }

        private void ThrowIfDisposed()
        {
            if (this.state >= STATE_DISPOSED)
                throw new WebSocketException("The write stream has been disposed.");
            if (this.state >= STATE_CLOSED)
                throw new WebSocketException("The write stream has been closed.");
        }

        public override Task CloseAsync()
        {
            return this.WriteAndCloseAsync(this.sendBuffer.Array, 0, 0, CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            var oldState = Interlocked.Exchange(ref this.state, STATE_DISPOSED);

            if (oldState == STATE_DISPOSED)
                return;

            if (this.webSocket.Connection.IsConnected == false)
                return;

            if (oldState != STATE_CLOSED)
            {
                this.webSocket.Connection.Log.Warning($"Disposed() is called on non-closed {this.GetType()}. Call {nameof(this.CloseAsync)}() or {nameof(this.WriteAndCloseAsync)}() " +
                    "before disposing stream or connections will randomly break.");
                this.webSocket.Connection.CloseAsync(WebSocketCloseReasons.ProtocolError).Wait();
            }
        }
    }
}
