using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Sudoku.Server.Network
{
    internal class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public ClientHandler(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public async Task HandleAsync()
        {
            Console.WriteLine(
                "[CLIENT] Handler started: "
                + _client.Client.RemoteEndPoint
            );

            byte[] buffer = new byte[4096];

            try
            {
                while (_client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(
                        buffer,
                        0,
                        buffer.Length
                    );

                    // Client đã ngắt kết nối
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    string message = Encoding.UTF8.GetString(
                        buffer,
                        0,
                        bytesRead
                    );

                    Console.WriteLine(
                        "[CLIENT] Received: " + message
                    );

                    // Phản hồi lại client
                    await SendAsync(
                        "Server received: " + message
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[CLIENT] Error: " + ex.Message
                );
            }
            finally
            {
                Close();
            }
        }

        public async Task SendAsync(string message)
        {
            if (!_client.Connected)
                return;

            byte[] data = Encoding.UTF8.GetBytes(message);

            await _stream.WriteAsync(
                data,
                0,
                data.Length
            );
        }

        public void Close()
        {
            try
            {
                _stream.Close();
                _client.Close();

                Console.WriteLine(
                    "[CLIENT] Disconnected."
                );
            }
            catch
            {
                // Bỏ qua lỗi khi đóng client
            }
        }
    }
}