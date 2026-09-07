using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sudoku.Mobile.Network;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
    {
#if ANDROID
        string baseUrl = "http://10.0.2.2:5243/";
#else
        string baseUrl = "https://localhost:7169/";
#endif

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data)
    {
        try
        {
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                endpoint,
                jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var jsonString =
                    await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<TResponse>(
                    jsonString,
                    options);
            }

            Console.WriteLine(
                $"[API] Status code: {(int)response.StatusCode}");

            return default;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[API LỖI] {ex.GetType().Name}: {ex.Message}");

            return default;
        }
    }
}