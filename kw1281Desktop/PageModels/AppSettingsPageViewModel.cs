using kw1281Desktop.Models.Base;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace kw1281Desktop.PageModels;

public sealed partial class AppSettingsPageViewModel : BasePropertyChanged
{
    public ObservableCollection<int> Bauds { get; } = [4800, AppSettings.BaudDefault, 10400];
    public ObservableCollection<string> Ports { get; } = [AppSettings.PortDefault, "COM2", "COM3", "COM4"];

    public AppSettingsPageViewModel()
    {
        InitSettings();
    }

    private int _selectedBaud;
    public int SelectedBaud
    {
        get => _selectedBaud;
        set
        {
            SetProperty(ref _selectedBaud, value);
            AppSettings.Baud = value;
            AppSettingsStorage.Save("baud", value);
        }
    }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            SetProperty(ref _selectedPort, value);
            AppSettings.Port = value;
            AppSettingsStorage.Save("port", value);
        }
    }

    private bool _isLoggingEnabled;

    public bool IsLoggingEnabled
    {
        get => _isLoggingEnabled;
        set {
            SetProperty(ref _isLoggingEnabled, value);
            AppSettings.Logging = value;
            AppSettingsStorage.Save("logging", value);
        }
    }

    private void InitSettings()
    {
        Dictionary<string, object>? loaded = AppSettingsStorage.Load();

        SelectedPort = loaded?["port"] != null ? loaded["port"].ToString() : AppSettings.PortDefault;
        IsLoggingEnabled = loaded?["logging"] == null || ((JsonElement)loaded["logging"]).GetBoolean();
        SelectedBaud = loaded?.TryGetValue("baud", out object? value) != null && value != null
            ? ((JsonElement)value).GetInt32()
            : AppSettings.BaudDefault;
    }
}