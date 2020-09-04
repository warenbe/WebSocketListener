using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace vtortola.WebSockets
{
    public static class WebSocketStringExtensions
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        [NotNull, ItemCanBeNull]
        public static async Task<string> ReadStringAsync([NotNull] this WebSocket webSocket, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));

            using (var readStream = await webSocket.ReadMessageAsync(cancellationToken).ConfigureAwait(false))
            {
                if (readStream == null)
                    return null;

                using (var reader = new StreamReader(readStream, Utf8NoBom))
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        public static async Task WriteStringAsync([NotNull] this WebSocket webSocket, [NotNull] string data, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));
            if (data == null) throw new ArgumentNullException(nameof(data));

            cancellationToken.ThrowIfCancellationRequested();

            using (var msg = webSocket.CreateMessageWriter(WebSocketMessageType.Text))
            using (var writer = new StreamWriter(msg, Utf8NoBom))
            {
                await writer.WriteAsync(data).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                await msg.CloseAsync().ConfigureAwait(false);
            }
        }

        public static async Task WriteStringAsync([NotNull] this WebSocket webSocket, [NotNull] char[] data, int offset, int count, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));

            cancellationToken.ThrowIfCancellationRequested();

            using (var msg = webSocket.CreateMessageWriter(WebSocketMessageType.Text))
            using (var writer = new StreamWriter(msg, Utf8NoBom))
            {
                await writer.WriteAsync(data, offset, count).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                await msg.CloseAsync().ConfigureAwait(false);
            }
        }

        public static Task WriteBytesAsync([NotNull] this WebSocket webSocket, [NotNull] byte[] data, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            return WriteBytesAsync(webSocket, data, 0, data.Length, cancellationToken);
        }

        public static async Task WriteBytesAsync([NotNull] this WebSocket webSocket, [NotNull] byte[] data, int offset, int count, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (webSocket == null) throw new ArgumentNullException(nameof(webSocket));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));

            cancellationToken.ThrowIfCancellationRequested();

            using (var writer = webSocket.CreateMessageWriter(WebSocketMessageType.Binary))
            {
                await writer.WriteAndCloseAsync(data, offset, count, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
