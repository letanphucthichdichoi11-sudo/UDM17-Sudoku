using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sudoku.Server.Game;
using Sudoku.Server.Network;
using Sudoku.Shared.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Utils;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class NetworkProtocolTests
    {
        [TestMethod]
        public async Task PacketCodec_FragmentedReadsAndMultipleFrames_RoundTrip()
        {
            Message first = Message.Create(MessageType.Heartbeat, new EmptyPayload());
            Message second = Message.Create(MessageType.ListRooms, new EmptyPayload());
            var output = new MemoryStream();
            await PacketCodec.WriteMessageAsync(output, first, CancellationToken.None);
            await PacketCodec.WriteMessageAsync(output, second, CancellationToken.None);

            var input = new FragmentedReadStream(output.ToArray(), 1);
            Message readFirst = await PacketCodec.ReadMessageAsync(input, CancellationToken.None);
            Message readSecond = await PacketCodec.ReadMessageAsync(input, CancellationToken.None);

            Assert.AreEqual(first.MessageId, readFirst.MessageId);
            Assert.AreEqual(second.MessageId, readSecond.MessageId);
            Assert.AreEqual(MessageType.Heartbeat, readFirst.Type);
            Assert.AreEqual(MessageType.ListRooms, readSecond.Type);
        }

        [TestMethod]
        public async Task PacketCodec_RejectsZeroAndOversizedFrames()
        {
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                PacketCodec.ReadMessageAsync(
                    new MemoryStream(new byte[] { 0, 0, 0, 0 }),
                    CancellationToken.None));

            int oversized = Packet.MaximumPayloadBytes + 1;
            byte[] header =
            {
                (byte)(oversized >> 24), (byte)(oversized >> 16),
                (byte)(oversized >> 8), (byte)oversized
            };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                PacketCodec.ReadMessageAsync(new MemoryStream(header), CancellationToken.None));
        }

        [TestMethod]
        public void MatchStatusContract_RoundTripsPuzzleWithoutSolution()
        {
            var status = new MatchStatusResponse
            {
                MatchId = Guid.NewGuid(),
                RoomId = Guid.NewGuid(),
                Puzzle = new int[81],
                Duration = MatchDurationMinutes.Five,
                State = MatchLifecycleState.Preparing,
                ServerUtcNow = DateTime.UtcNow
            };
            status.Puzzle[2 * 9 + 3] = 7;

            MatchStatusResponse roundTrip = JsonHelper.Deserialize<MatchStatusResponse>(
                JsonHelper.Serialize(status));

            Assert.AreEqual(7, roundTrip.Puzzle[2 * 9 + 3]);
            Assert.IsNull(typeof(MatchStatusResponse).GetProperty("Solution"));
            Assert.IsNull(typeof(MatchStatusResponse).GetProperty("SolutionGrid"));
        }

        [TestMethod]
        public async Task TcpServer_RequiresHandshakeThenSupportsLobbyAndReconnect()
        {
            int port = GetAvailablePort();
            using (var games = new GameApplicationServices())
            using (var server = new Sudoku.Server.Network.Server(games, port))
            {
                Task serverTask = server.StartAsync();
                await Task.Delay(100);

                HandshakeResponse handshake;
                using (var firstClient = new TcpClient())
                {
                    await firstClient.ConnectAsync(IPAddress.Loopback, port);
                    NetworkStream stream = firstClient.GetStream();

                    Message unauthorized = await RequestAsync(
                        stream, Message.Create(MessageType.ListRooms, new EmptyPayload()));
                    Assert.AreEqual(MessageType.Error, unauthorized.Type);
                    Assert.AreEqual(ProtocolErrorCode.Unauthorized, unauthorized.Error.Code);

                    Message handshakeMessage = await RequestAsync(
                        stream, Message.Create(MessageType.Handshake,
                            new HandshakeRequest { PlayerId = "network-player", PlayerName = "Network Player" }));
                    handshake = handshakeMessage.ReadPayload<HandshakeResponse>();
                    Assert.IsFalse(String.IsNullOrWhiteSpace(handshake.SessionToken));

                    Message create = await RequestAsync(
                        stream, Message.Create(MessageType.CreateRoom,
                            new CreateRoomRequest { RoomName = "TCP Room" }));
                    LobbyRoomDto room = create.ReadPayload<LobbyRoomDto>();
                    Assert.AreEqual("TCP Room", room.RoomName);
                    Assert.AreEqual("network-player", room.Players[0].PlayerId);
                }

                await Task.Delay(100);
                using (var reconnectClient = new TcpClient())
                {
                    await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                    Message reconnect = await RequestAsync(
                        reconnectClient.GetStream(),
                        Message.Create(MessageType.Reconnect,
                            new ReconnectRequest { SessionToken = handshake.SessionToken }));
                    Assert.AreEqual(MessageType.ReconnectAccepted, reconnect.Type);
                }

                server.Stop();
                await serverTask;
            }
        }

        private static async Task<Message> RequestAsync(NetworkStream stream, Message request)
        {
            await PacketCodec.WriteMessageAsync(stream, request, CancellationToken.None);
            while (true)
            {
                Message response = await PacketCodec.ReadMessageAsync(stream, CancellationToken.None);
                if (response.CorrelationId == request.MessageId) return response;
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class FragmentedReadStream : MemoryStream
        {
            private readonly int _maximumRead;

            public FragmentedReadStream(byte[] bytes, int maximumRead) : base(bytes)
            {
                _maximumRead = maximumRead;
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count,
                CancellationToken cancellationToken)
            {
                return base.ReadAsync(
                    buffer, offset, Math.Min(count, _maximumRead), cancellationToken);
            }
        }
    }
}
