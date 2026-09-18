using System;
using Sudoku.Shared.Utils;

namespace Sudoku.Shared.Network
{
    public sealed class Message
    {
        public const int CurrentProtocolVersion = 1;

        public int ProtocolVersion { get; set; }
        public Guid MessageId { get; set; }
        public Guid? CorrelationId { get; set; }
        public MessageType Type { get; set; }
        public DateTime SentAtUtc { get; set; }
        public string Payload { get; set; }
        public ProtocolError Error { get; set; }

        public static Message Create<T>(MessageType type, T payload)
        {
            return new Message
            {
                ProtocolVersion = CurrentProtocolVersion,
                MessageId = Guid.NewGuid(),
                Type = type,
                SentAtUtc = DateTime.UtcNow,
                Payload = payload == null ? null : JsonHelper.Serialize(payload)
            };
        }

        public T ReadPayload<T>()
        {
            return JsonHelper.Deserialize<T>(Payload);
        }
    }

    public sealed class ProtocolError
    {
        public ProtocolErrorCode Code { get; set; }
        public string Message { get; set; }
    }

    public enum ProtocolErrorCode
    {
        InvalidPacket,
        UnsupportedProtocol,
        Unauthorized,
        InvalidPayload,
        RoomNotFound,
        RoomFull,
        MatchNotFound,
        MatchPreparing,
        MatchExpired,
        GivenCellLocked,
        IncorrectValue,
        InternalError
    }
}
