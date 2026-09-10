using System.Net;
using System.Text;
using System.Text.Json;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Network;

public sealed class ApiResult<T>
{
    public T? Value { get; init; }
    public ApiErrorKind ErrorKind { get; init; }
    public HttpStatusCode? StatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public Exception? Exception { get; init; }
    public bool IsSuccess => ErrorKind == ApiErrorKind.None && Value != null;
}

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
        : this(CreateDefaultHttpClient())
    {
    }

    internal ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        string? configuredUrl =
            Environment.GetEnvironmentVariable("SUDOKU_API_URL");

#if ANDROID
        const string platformDefaultUrl = "http://10.0.2.2:5243/";
#else
        // Avoid a development-certificate dependency in unpackaged Windows builds.
        const string platformDefaultUrl = "http://127.0.0.1:5243/";
#endif

        string baseUrl = string.IsNullOrWhiteSpace(configuredUrl)
            ? platformDefaultUrl
            : configuredUrl;

        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        Console.WriteLine($"[API] Base URL: {baseUrl}");

        return new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data)
    {
        ApiResult<TResponse> result =
            await PostDetailedAsync<TRequest, TResponse>(endpoint, data);
        return result.Value;
    }

    public async Task<ApiResult<TResponse>> PostDetailedAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data,
        CancellationToken cancellationToken = default)
    {
        Uri requestUri = new(_httpClient.BaseAddress!, endpoint);
        Console.WriteLine($"[API] POST {requestUri}");
        Console.WriteLine($"[API] Payload: {typeof(TRequest).Name} (sensitive fields redacted)");

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(
                endpoint,
                content,
                cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            Console.WriteLine($"[API] Status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
#if DEBUG
                Console.WriteLine($"[API] Error response: {body}");
#endif
                return new ApiResult<TResponse>
                {
                    ErrorKind = ApiErrorClassifier.FromStatusCode(response.StatusCode),
                    StatusCode = response.StatusCode,
                    ResponseBody = body
                };
            }

            try
            {
                TResponse? value = JsonSerializer.Deserialize<TResponse>(
                    body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return value == null
                    ? new ApiResult<TResponse>
                    {
                        ErrorKind = ApiErrorKind.InvalidResponse,
                        StatusCode = response.StatusCode,
                        ResponseBody = body
                    }
                    : new ApiResult<TResponse>
                    {
                        Value = value,
                        ErrorKind = ApiErrorKind.None,
                        StatusCode = response.StatusCode
                    };
            }
            catch (JsonException exception)
            {
                Console.WriteLine($"[API] Invalid JSON: {exception.Message}");
                return new ApiResult<TResponse>
                {
                    ErrorKind = ApiErrorKind.InvalidResponse,
                    StatusCode = response.StatusCode,
                    ResponseBody = body,
                    Exception = exception
                };
            }
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[API] Timeout: {exception.Message}");
            return new ApiResult<TResponse>
            {
                ErrorKind = ApiErrorKind.Timeout,
                Exception = exception
            };
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"[API] Connection error: {exception.Message}");
            return new ApiResult<TResponse>
            {
                ErrorKind = ApiErrorKind.Connection,
                StatusCode = exception.StatusCode,
                Exception = exception
            };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[API] Unexpected {exception.GetType().Name}: {exception.Message}");
            return new ApiResult<TResponse>
            {
                ErrorKind = ApiErrorKind.Unknown,
                Exception = exception
            };
        }
    }

}
