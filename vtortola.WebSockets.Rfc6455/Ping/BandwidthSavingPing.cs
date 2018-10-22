using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace vtortola.WebSockets.Rfc6455
{
    partial class WebSocketConnectionRfc6455
    {
        private class BandwidthSavingPing : PingHandler
        {
            private readonly ArraySegment<byte> pingBuffer;
            private readonly TimeSpan pingTimeout;
            private readonly WebSocketConnectionRfc6455 connection;
            private TimeSpan lastActivityTime;

            public BandwidthSavingPing(WebSocketConnectionRfc6455 connection)
            {
                if (connection == null) throw new ArgumentNullException(nameof(connection));

                this.pingTimeout = connection.options.PingTimeout;
                if (this.pingTimeout < TimeSpan.Zero)
                    this.pingTimeout = TimeSpan.MaxValue;

                this.pingBuffer = connection.outPingBuffer;
                this.connection = connection;
                this.NotifyActivity();
            }

            public sealed override async Task PingAsync()
            {
                if (this.connection.IsClosed)
                    return;

                var elapsedTime = TimestampToTimeSpan(Stopwatch.GetTimestamp()) - this.lastActivityTime;
                if (elapsedTime > this.pingTimeout)
                {
                    if (this.connection.log.IsDebugEnabled)
                        this.connection.log.Debug($"WebSocket connection ({this.connection.GetHashCode():X}) has been closed due ping timeout. Time elapsed: {elapsedTime}, timeout: {this.pingTimeout}.");

                    await this.connection.CloseAsync(WebSocketCloseReasons.GoingAway).ConfigureAwait(false);
                    return;
                }

                var messageType = (WebSocketMessageType)WebSocketFrameOption.Ping;
                var pingFrame = this.connection.PrepareFrame(this.pingBuffer, 0, true, false, messageType, WebSocketExtensionFlags.None);
                await this.connection.SendFrameAsync(pingFrame, TimeSpan.Zero, SendOptions.NoErrors, CancellationToken.None).ConfigureAwait(false);
            }

            /// <inheritdoc />
            public sealed override void NotifyActivity()
            {
                this.lastActivityTime = TimestampToTimeSpan(Stopwatch.GetTimestamp());
            }
            public sealed override void NotifyPong(ArraySegment<byte> pongBuffer)
            {
                this.NotifyActivity();
            }
        }
    }
}
