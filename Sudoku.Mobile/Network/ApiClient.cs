using System.Net.Http.Json;
namespace Sudoku.Mobile.Network;
public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
    {
#if ANDROID
        // Android Emulator:
        // 10.0.2.2 trỏ về máy tính đang chạy Server
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://10.0.2.2:5000/")
        };
#else
        // Windows / các platform khác
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5000/")
        };
#endif

        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }


    // GET request
    public async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<T>(endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GET Error: {ex.Message}");
            return default;
        }
    }
    // POST request
    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data)
    {
        try
        {
            HttpResponseMessage response =
                await _httpClient.PostAsJsonAsync(endpoint, data);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"POST Error: {(int)response.StatusCode}");

                return default;
            }

            return await response.Content
                .ReadFromJsonAsync<TResponse>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"POST Error: {ex.Message}");
            return default;
        }
    }

    // Kiểm tra Server
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            HttpResponseMessage response =
                await _httpClient.GetAsync("api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}