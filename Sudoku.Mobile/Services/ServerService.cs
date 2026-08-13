using Sudoku.Mobile.Network;

namespace Sudoku.Mobile.Services;

public class ServerService
{
    private readonly ApiClient _apiClient;

    public ServerService()
    {
        _apiClient = new ApiClient();
    }

    public async Task<bool> CheckServerConnectionAsync()
    {
        try
        {
            return await _apiClient.CheckConnectionAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            return await _apiClient.GetAsync<T>(endpoint);
        }
        catch
        {
            return default;
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data)
    {
        try
        {
            return await _apiClient.PostAsync<TRequest, TResponse>(
                endpoint,
                data);
        }
        catch
        {
            return default;
        }
    }
}