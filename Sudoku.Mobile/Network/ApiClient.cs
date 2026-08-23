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
                    // 1. Đọc bức thư Backend gửi về dưới dạng chuỗi nguyên bản
                    var jsonString = await response.Content.ReadAsStringAsync();

                    // 2. Ép nó phải bỏ qua việc phân biệt chữ Hoa/chữ thường
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    // 3. Dịch bức thư sang Object và trả về cho app
                    return System.Text.Json.JsonSerializer.Deserialize<TResponse>(jsonString, options);
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