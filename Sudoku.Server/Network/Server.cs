using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sudoku.Server.Network
{
    internal class Server
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentBag<TcpClient> _clients;

        private CancellationTokenSource _cancellationTokenSource;

        public int Port { get; private set; }

        public bool IsRunning { get; private set; }

        public Server(int port = 5000)
        {
            Port = port;

            _listener = new TcpListener(
                IPAddress.Any,
                Port
            );

            _clients = new ConcurrentBag<TcpClient>();
        }

        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            _cancellationTokenSource =
                new CancellationTokenSource();

            _listener.Start();

            IsRunning = true;

            Console.WriteLine(
                "[SERVER] Started on port " + Port
            );

            try
            {
                while (
                    !_cancellationTokenSource.Token
                        .IsCancellationRequested
                )
                {
                    TcpClient client =
                        await _listener.AcceptTcpClientAsync();

                    _clients.Add(client);

                    Console.WriteLine(
                        "[SERVER] Client connected: "
                        + client.Client.RemoteEndPoint
                    );

                    // KAN-5:
                    // ClientHandler sẽ xử lý client
                    // sau khi task KAN-5 được thực hiện.
                }
            }
            catch (ObjectDisposedException)
            {
                // Server đã được Stop()
            }
            catch (SocketException)
            {
                // Listener đã được đóng khi Stop()
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[SERVER] Error: " + ex.Message
                );
            }
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            IsRunning = false;

            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }

            _listener.Stop();

            foreach (TcpClient client in _clients)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // Bỏ qua lỗi khi đóng client
                }
            }

            Console.WriteLine(
                "[SERVER] Stopped."
            );
        }
    }
}