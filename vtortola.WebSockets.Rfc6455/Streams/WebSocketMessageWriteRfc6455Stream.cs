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

        private bool _isHeaderSent;
        private int _internalUsedBufferLength;
        private volatile int state = STATE_OPEN;

        private readonly WebSocketRfc6455 _webSocket;
        private readonly WebSocketMessageType _messageType;

        public WebSocketMessageWriteRfc6455Stream(WebSocketRfc6455 webSocket, WebSocketMessageType messageType)
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));

            this._internalUsedBufferLength = 0;
            this._messageType = messageType;
            this._webSocket = webSocket;
        }
        public WebSocketMessageWriteRfc6455Stream(WebSocketRfc6455 webSocket, WebSocketMessageType messageType, WebSocketExtensionFlags extensionFlags)
            : this(webSocket, messageType)
        {
            this.ExtensionFlags.Rsv1 = extensionFlags.Rsv1;
            this.ExtensionFlags.Rsv2 = extensionFlags.Rsv2;
            this.ExtensionFlags.Rsv3 = extensionFlags.Rsv3;
        }

        private void BufferData(byte[] buffer, ref int offset, ref int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            var read = Math.Min(count, this._webSocket.Connection.SendBuffer.Count - this._internalUsedBufferLength);
            if (read == 0)
                return;
            Array.Copy(buffer, offset, this._webSocket.Connection.SendBuffer.Array, this._webSocket.Connection.SendBuffer.Offset + this._internalUsedBufferLength, read);
            this._internalUsedBufferLength += read;
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
                this.BufferData(buffer, ref offset, ref count);

                if (this._internalUsedBufferLength == this._webSocket.Connection.SendBuffer.Count && count > 0)
                {
                    var dataFrame = this._webSocket.Connection.PrepareFrame(this._webSocket.Connection.SendBuffer, this._internalUsedBufferLength, false, this._isHeaderSent, this._messageType, this.ExtensionFlags);
                    await this._webSocket.Connection.SendFrameAsync(dataFrame, cancellationToken).ConfigureAwait(false);
                    this._internalUsedBufferLength = 0;
                    this._isHeaderSent = true;
                }
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            if (this._internalUsedBufferLength <= 0 && this._isHeaderSent)
                return;

            var dataFrame = this._webSocket.Connection.PrepareFrame(this._webSocket.Connection.SendBuffer, this._internalUsedBufferLength, false, this._isHeaderSent, this._messageType, this.ExtensionFlags);
            await this._webSocket.Connection.SendFrameAsync(dataFrame, cancellationToken).ConfigureAwait(false);
            this._internalUsedBufferLength = 0;
            this._isHeaderSent = true;
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
            if (Interlocked.CompareExchange(ref this.state, STATE_CLOSED, STATE_OPEN) != STATE_OPEN)
                return TaskHelper.CompletedTask;

            var dataFrame = this._webSocket.Connection.PrepareFrame(this._webSocket.Connection.SendBuffer, this._internalUsedBufferLength, true, this._isHeaderSent, this._messageType, this.ExtensionFlags);
            var sendFrameTask = this._webSocket.Connection.SendFrameAsync(dataFrame, CancellationToken.None);

            sendFrameTask.ContinueWith(
                (sendTask, s) => ((WebSocketConnectionRfc6455)s).EndWriting(),
                this._webSocket.Connection,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return sendFrameTask;
        }

        protected override void Dispose(bool disposing)
        {
            // don't do flush on close if connection is broken
            if (this._webSocket.Connection.IsConnected)
            {
                var closeTask = this.CloseAsync();
                if (closeTask.IsCompleted == false && this._webSocket.Connection.Log.IsWarningEnabled)
                {
                    this._webSocket.Connection.Log.Warning(
                        $"Invoking asynchronous operation '{nameof(this.CloseAsync)}()' from synchronous method '{nameof(Dispose)}(bool {nameof(disposing)})'. " +
                        $"Call and await {nameof(this.CloseAsync)}() before disposing stream.");
                }

                if (closeTask.Status != TaskStatus.RanToCompletion)
                {
                    closeTask.Wait(); // make exception observed
                }
            }
            Interlocked.Exchange(ref this.state, STATE_DISPOSED);
        }
    }
}
