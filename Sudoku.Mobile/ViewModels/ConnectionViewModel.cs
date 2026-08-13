using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sudoku.Mobile.Services;

namespace Sudoku.Mobile.ViewModels;

public class ConnectionViewModel : INotifyPropertyChanged
{
    private readonly ServerService _serverService;

    private string _status = "Disconnected";
    private bool _isConnected;
    private bool _isConnecting;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConnectionViewModel()
    {
        _serverService = new ServerService();
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
                return;

            _isConnected = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (_isConnecting == value)
                return;

            _isConnecting = value;
            OnPropertyChanged();
        }
    }

    public async Task CheckConnectionAsync()
    {
        if (IsConnecting)
            return;

        IsConnecting = true;

        Status = "Connecting...";
        IsConnected = false;

        bool connected =
            await _serverService.CheckServerConnectionAsync();

        if (connected)
        {
            IsConnected = true;
            Status = "Connection Restored";
        }
        else
        {
            IsConnected = false;
            Status = "Server Error";
        }

        IsConnecting = false;
    }

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}