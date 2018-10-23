/*
	Copyright (c) 2017 Denis Zykov
	License: https://opensource.org/licenses/MIT
*/
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using vtortola.WebSockets.Tools;

namespace vtortola.WebSockets.Rfc6455
{
    partial class WebSocketConnectionRfc6455
    {
        private sealed class ManualPing : PingHandler
        {
            private readonly TimeSpan pingTimeout;
            private readonly ArraySegment<byte> pingBuffer;
            private readonly WebSocketConnectionRfc6455 connection;
            private TimeSpan lastActivityTime;

            public ManualPing(WebSocketConnectionRfc6455 connection)
            {
                if (connection == null) throw new ArgumentNullException(nameof(connection));

                this.pingTimeout = connection.options.PingTimeout;
                if (this.pingTimeout < TimeSpan.Zero)
                    this.pingTimeout = TimeSpan.MaxValue;

                this.pingBuffer = connection.outPingBuffer;
                this.connection = connection;
                this.NotifyActivity();
            }

            public override async Task PingAsync()
            {
                if (this.connection.IsClosed)
                    return;

                var elapsedTime = TimestampToTimeSpan(Stopwatch.GetTimestamp()) - this.lastActivityTime;
                if (elapsedTime > this.pingTimeout)
                {
                    SafeEnd.Dispose(this.connection);

                    if (this.connection.log.IsDebugEnabled)
                        this.connection.log.Debug($"WebSocket connection ({this.connection.GetHashCode():X}) has been closed due ping timeout. Time elapsed: {elapsedTime}, timeout: {this.pingTimeout}.");

                    return;
                }

                var messageType = (WebSocketMessageType)WebSocketFrameOption.Ping;
                var count = this.pingBuffer.Array[this.pingBuffer.Offset];
                var payload = this.pingBuffer.Skip(1);

                var pingFrame = this.connection.PrepareFrame(payload, count, true, false, messageType, WebSocketExtensionFlags.None);
                await this.connection.SendFrameAsync(pingFrame, TimeSpan.Zero, SendOptions.NoErrors, CancellationToken.None).ConfigureAwait(false);
            }
            /// <inheritdoc />
            public override void NotifyActivity()
            {
                this.lastActivityTime = TimestampToTimeSpan(Stopwatch.GetTimestamp());
            }
            public override void NotifyPong(ArraySegment<byte> pongBuffer)
            {
                this.NotifyActivity();
            }
        }
    }
}
