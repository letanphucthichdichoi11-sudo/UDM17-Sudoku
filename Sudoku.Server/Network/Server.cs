using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sudoku.Server.Game;

namespace Sudoku.Server.Network
{
    internal sealed class Server : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<Guid, ClientHandler> _clients =
            new ConcurrentDictionary<Guid, ClientHandler>();
        private readonly MessageDispatcher _dispatcher;
        private CancellationTokenSource _cancellation;

        public int Port { get; private set; }
        public bool IsRunning { get; private set; }

        public Server(GameApplicationServices games, int port = 5000)
        {
            Port = port;
            _listener = new TcpListener(IPAddress.Any, port);
            var sessions = new SessionManager();
            _dispatcher = new MessageDispatcher(games, sessions, games.Clock);
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;
            _cancellation = new CancellationTokenSource();
            _listener.Start();
            IsRunning = true;

            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    var handler = new ClientHandler(client, _dispatcher);
                    _clients[handler.ConnectionId] = handler;
                    RunClientAsync(handler);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { if (IsRunning) throw; }
            finally { IsRunning = false; }
        }

        private async void RunClientAsync(ClientHandler handler)
        {
            try { await handler.HandleAsync(); }
            finally
            {
                ClientHandler removed;
                _clients.TryRemove(handler.ConnectionId, out removed);
                handler.Dispose();
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cancellation.Cancel();
            _listener.Stop();
            foreach (ClientHandler client in _clients.Values) client.Dispose();
            _clients.Clear();
        }

        public void Dispose()
        {
            Stop();
            if (_cancellation != null) _cancellation.Dispose();
        }
    }
}
