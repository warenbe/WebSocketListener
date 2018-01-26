using System;
using System.Diagnostics;

namespace vtortola.WebSockets.Rfc6455
{
    internal sealed class WebSocketFrameHeader
    {
        public uint MaskKey;
        private int cursor;

        public long ContentLength { get; private set; }
        public int HeaderLength { get; private set; }
        public WebSocketFrameHeaderFlags Flags { get; private set; }
        public long RemainingBytes { get; private set; }

        public void DecodeBytes(byte[] buffer, int offset, int count)
        {
            this.EncodeBytes(buffer, offset, count);
        }
        public void EncodeBytes(byte[] buffer, int offset, int count)
        {
            this.RemainingBytes -= count;

            if (!this.Flags.MASK) return;

            MaskPayload(buffer, offset, count, this.MaskKey, this.cursor);
            this.cursor += count;
        }

        public int WriteTo(byte[] buffer, int offset)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            this.Flags.ToBytes(this.ContentLength, buffer, offset);
            var written = 2;
            if (this.ContentLength <= 125)
            {
                // header length is included in the 2b header
            }
            else if (this.ContentLength <= ushort.MaxValue)
            {
                ((ushort)this.ContentLength).ToBytesBackwards(buffer, offset + written);
                written += 2;
            }
            else
            {
                ((ulong)this.ContentLength).ToBytesBackwards(buffer, offset + written);
                written += 8;
            }
            // write mask is it is masked message
            if (this.Flags.MASK)
            {
               this.MaskKey.ToBytes(buffer, offset + written);
               written += 4;
            }
            return written;
        }

        public static int GetHeaderLength(byte[] frameStart, int offset)
        {
            if (frameStart == null || frameStart.Length < offset + 2)
                throw new WebSocketException("The first two bytes of a header are required to understand the header length");

            var value = frameStart[offset + 1];
            var isMasked = value >= 128;
            var contentLength = isMasked ? value - 128 : value;

            if (contentLength <= 125)
                return isMasked ? 6 : 2;
            else if (contentLength == 126)
                return (isMasked ? 6 : 2) + 2;
            else if (contentLength == 127)
                return (isMasked ? 6 : 2) + 8;
            else
                throw new WebSocketException("Cannot understand a length field of " + contentLength);
        }
        public static bool TryParse(byte[] frameStart, int offset, int headerLength, out WebSocketFrameHeader header)
        {
            if (frameStart == null) throw new ArgumentNullException(nameof(frameStart));

            header = null;

            var availableLength = frameStart.Length - offset;
            if (availableLength < 6 || availableLength < headerLength)
                return false;

            var value = frameStart[offset + 1];
            var contentLength = (long)(value >= 128 ? value - 128 : value);

            if (contentLength <= 125)
            {
                // small frame
            }
            else if (contentLength == 126)
            {
                if (availableLength < headerLength)
                    return false;

                if (BitConverter.IsLittleEndian)
                    frameStart.ReversePortion(offset + 2, 2);
                contentLength = BitConverter.ToUInt16(frameStart, offset + 2);
            }
            else if (contentLength == 127)
            {
                if (availableLength < headerLength)
                    return false;

                if (BitConverter.IsLittleEndian)
                    frameStart.ReversePortion(offset + 2, 8);

                var length = BitConverter.ToUInt64(frameStart, offset + 2);
                if (length > long.MaxValue)
                {
                    throw new WebSocketException("The maximum supported frame length is 9223372036854775807, current frame is " + length.ToString());
                }

                contentLength = (long)length;
            }
            else
                return false;

            WebSocketFrameHeaderFlags flags;
            if (!WebSocketFrameHeaderFlags.TryParse(frameStart, offset, out flags))
            {
                return false;
            }

            var maskKey = 0U;
            if (flags.MASK)
            {
                headerLength -= 4;
                if (BitConverter.IsLittleEndian == false)
                    Array.Reverse(frameStart, offset + headerLength, 4);

                maskKey = BitConverter.ToUInt32(frameStart, offset + headerLength);
            }

            header = new WebSocketFrameHeader
            {
                ContentLength = contentLength,
                HeaderLength = headerLength,
                Flags = flags,
                RemainingBytes = contentLength,
                MaskKey = maskKey
            };

            return true;
        }
        public static WebSocketFrameHeader Create(long contentLength, bool isComplete, bool headerSent, uint maskKey, WebSocketFrameOption option, WebSocketExtensionFlags extensionFlags)
        {
            if (extensionFlags == null) throw new ArgumentNullException(nameof(extensionFlags));

            var isMasked = maskKey != 0;
            var flags = new WebSocketFrameHeaderFlags(isComplete, isMasked, headerSent ? WebSocketFrameOption.Continuation : option, extensionFlags);

            int headerLength;
            if (contentLength <= 125)
                headerLength = 2;
            else if (contentLength <= ushort.MaxValue)
                headerLength = 4;
            else
                headerLength = 10;

            if (isMasked)
                headerLength += 4;

            return new WebSocketFrameHeader
            {
                HeaderLength = headerLength,
                ContentLength = contentLength,
                Flags = flags,
                RemainingBytes = contentLength,
                MaskKey = maskKey
            };
        }

        private static void MaskPayload(byte[] bytes, int offset, int count, uint maskKey, int maskOffset = 0)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            if (maskKey == 0)
                return;

            if (BitConverter.IsLittleEndian == false)
                maskKey = ReverseBytes(maskKey); // reverse to little-endian


            if (maskOffset != 0)
            {
                switch (4 - maskOffset % 4)
                {
                    case 4: break;
                    case 3: bytes[offset] = (byte)(bytes[offset] ^ ((maskKey >> 8) & byte.MaxValue)); offset++; count--; if (count == 0) break; else goto case 2;
                    case 2: bytes[offset] = (byte)(bytes[offset] ^ ((maskKey >> 16) & byte.MaxValue)); offset++; count--; if (count == 0) break; else goto case 1;
                    case 1: bytes[offset] = (byte)(bytes[offset] ^ ((maskKey >> 24) & byte.MaxValue)); offset++; count--; break;
                }
            }

            var quartetsCount = count / 4;
            var bytesLeft = count - (quartetsCount * 4);

            if (quartetsCount > 0)
            {
                unsafe
                {
                    fixed (byte* bufferStartPtr = bytes)
                    {
                        var payloadPtr = (uint*)(bufferStartPtr + offset);
                        for (var i = 0; i < quartetsCount; i++, payloadPtr++)
                            *payloadPtr = (*payloadPtr) ^ maskKey;
                    }
                    offset += quartetsCount * 4;
                    count -= quartetsCount * 4;
                }                
            }

            switch (bytesLeft)
            {
                case 3: bytes[offset + 2] = (byte)(bytes[offset + 2] ^ ((maskKey >> 16) & byte.MaxValue)); goto case 2;
                case 2: bytes[offset + 1] = (byte)(bytes[offset + 1] ^ ((maskKey >> 8) & byte.MaxValue)); goto case 1;
                case 1: bytes[offset + 0] = (byte)(bytes[offset + 0] ^ ((maskKey >> 0) & byte.MaxValue)); break;
            }
        }
        private static uint ReverseBytes(uint value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Flags.Option}, len: {this.ContentLength}, key: {this.MaskKey:X}, flags: {Flags}";
        }
    }
}
