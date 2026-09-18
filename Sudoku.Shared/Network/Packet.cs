using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sudoku.Shared.Utils;

namespace Sudoku.Shared.Network
{
    public sealed class Packet
    {
        public const int MaximumPayloadBytes = 1024 * 1024;
        public byte[] Payload { get; private set; }

        public Packet(byte[] payload)
        {
            if (payload == null || payload.Length == 0 ||
                payload.Length > MaximumPayloadBytes)
                throw new InvalidDataException("Packet payload length is invalid.");
            Payload = payload;
        }
    }

    public static class PacketCodec
    {
        public static async Task<Message> ReadMessageAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            byte[] lengthBytes = await ReadExactlyAsync(stream, 4, cancellationToken);
            int length = (lengthBytes[0] << 24) |
                         (lengthBytes[1] << 16) |
                         (lengthBytes[2] << 8) |
                         lengthBytes[3];
            if (length <= 0 || length > Packet.MaximumPayloadBytes)
                throw new InvalidDataException("Packet payload length is invalid.");

            byte[] payload = await ReadExactlyAsync(stream, length, cancellationToken);
            string json = Encoding.UTF8.GetString(payload);
            return JsonHelper.Deserialize<Message>(json);
        }

        public static async Task WriteMessageAsync(
            Stream stream,
            Message message,
            CancellationToken cancellationToken)
        {
            byte[] payload = Encoding.UTF8.GetBytes(JsonHelper.Serialize(message));
            var packet = new Packet(payload);
            int length = packet.Payload.Length;
            byte[] header =
            {
                (byte)(length >> 24),
                (byte)(length >> 16),
                (byte)(length >> 8),
                (byte)length
            };
            await stream.WriteAsync(header, 0, header.Length, cancellationToken);
            await stream.WriteAsync(packet.Payload, 0, packet.Payload.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static async Task<byte[]> ReadExactlyAsync(
            Stream stream,
            int length,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(
                    buffer, offset, length - offset, cancellationToken);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }
    }
}
