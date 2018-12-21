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
            private readonly TimeSpan pingInterval;
            private readonly WebSocketConnectionRfc6455 connection;
            private TimeSpan lastActivityTime;

            public BandwidthSavingPing(WebSocketConnectionRfc6455 connection)
            {
                if (connection == null) throw new ArgumentNullException(nameof(connection));

                this.pingTimeout = connection.options.PingTimeout;
                if (this.pingTimeout < TimeSpan.Zero)
                    this.pingTimeout = TimeSpan.MaxValue;
                this.pingInterval = connection.options.PingInterval;

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
                    SafeEnd.Dispose(this.connection);

                    if (this.connection.log.IsDebugEnabled)
                        this.connection.log.Debug($"WebSocket connection ({this.connection.GetHashCode():X}) has been closed due ping timeout. Time elapsed: {elapsedTime}, timeout: {this.pingTimeout}, interval: {this.pingInterval}.");

                    return;
                }

                var messageType = (WebSocketMessageType)WebSocketFrameOption.Ping;
                var pingFrame = this.connection.PrepareFrame(this.pingBuffer, 0, true, false, messageType, WebSocketExtensionFlags.None);
                var pingFrameLockTimeout = elapsedTime < this.pingInterval ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;

                //
                // ping_interval is 33% of ping_timeout time
                //
                // if elapsed_time < ping_interval then TRY to send ping frame
                // if elapsed_time > ping_interval then ENFORCE sending ping frame because ping_timeout is near
                //
                // pingFrameLockTimeout is controlling TRY/ENFORCE behaviour. Zero mean TRY to take write lock or skip. InfiniteTimeSpan mean wait indefinitely for write lock.
                //

                await this.connection.SendFrameAsync(pingFrame, pingFrameLockTimeout, SendOptions.NoErrors, CancellationToken.None).ConfigureAwait(false);
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
