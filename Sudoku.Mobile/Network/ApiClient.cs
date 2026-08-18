using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sudoku.Mobile.Network
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;

        public ApiClient()
        {
            string baseUrl = DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5243/" : "http://localhost:5243/";

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var jsonContent = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<TResponse>();
                }
                return default;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API LỖI] {ex.Message}");
                return default;
            }
        }
    }
}