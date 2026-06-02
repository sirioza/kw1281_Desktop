using BitFab.KW1281Test;
using BitFab.KW1281Test.Actions;
using BitFab.KW1281Test.Actions.Records;
using BitFab.KW1281Test.Enums;
using BitFab.KW1281Test.Models;
using kw1281Desktop.Converters;
using kw1281Desktop.Models;
using kw1281Desktop.Models.Base;
using System.Collections.ObjectModel;

namespace kw1281Desktop.PageModels.BasePageViewModels;

public abstract class BaseScanViewPageModel : BasePropertyChanged
{
    private event EventHandler? ScrollToLastRequested;
    private readonly ILoaderService _loader;

    protected Diagnostic Diagnostic { get; }

    protected BaseScanViewPageModel(Diagnostic diagnostic, ILoaderService loader)
    {
        var route = Shell.Current.CurrentState.Location.ToString();
        AppSettingsStorage.Save("page", route);

        Diagnostic = diagnostic;
        _loader = loader;
    }

    public ObservableCollection<LogLineDeck> LogLines { get; } = [];

    public ObservableCollection<ElementItem<int>> Addresses { get; } =
    [
        new ( 0x01, "01 - Engine" ),
        new ( 0x03, "03 - ABS Brakes" ),
        new ( 0x08, "08 - Auto HVAC" ),
        new ( 0x15, "15 - Airbags" ),
        new ( 0x17, "17 - Instruments" ),
        new ( 0x19, "19 - CAN Gateway" ),
        new ( 0x25, "25 - Immobilizer" ),
        new ( 0x37, "37 - Navigation" ),
        new ( 0x46, "46 - Cent. Conv." ),
        new ( 0x47, "47 - Sound System" ),
        new ( 0x56, "56 - Radio" ),
    ];

    protected virtual async Task ExecuteReadInBackground(int controllerAddress, Commands command, params Arg[] args)
    {
        await Task.Run(async () =>
        {
            async Task RunAsync() => await Diagnostic
                .RunAsync(AppSettings.Port!, AppSettings.Baud, controllerAddress, command, args);

            if (AppSettings.Logging)
            {
                using var messageScope = Messenger.Instance.BeginScope();
                try
                {
                    Messenger.Instance.MessageReceived += OnLogReceived;
                    await RunAsync();
                }
                finally
                {
                    Messenger.Instance.MessageReceived -= OnLogReceived;
                }
            }
            else
            {
                await RunAsync();
            }
        });
    }

    protected async Task ExecuteReadInBackgroundWithLoader(int controllerAddress, Commands command, params Arg[] args)
    {
        _loader.ShowAsync();

        try
        {
            await ExecuteReadInBackground(controllerAddress, command, args);
        }
        finally
        {
            await _loader.HideAsync();
        }
    }

    protected void OnResultReceived(IBaseResult baseResult)
    {
        LogLineDeck logLineDeck = new();

        if (baseResult.Ok)
        {
            logLineDeck.Text = baseResult.Content;
            logLineDeck.TextColor = Colors.Blue;
        }
        else if (!baseResult.Ok && baseResult is Result<Exception> ex)
        {
            logLineDeck.Text = $"{ex.Error.Message}.";
            logLineDeck.TextColor = Colors.Red;
        }
        else
        {
            logLineDeck.Text = "Data result error.";
            logLineDeck.TextColor = Colors.Red;
        }

        OnMessageReceived(logLineDeck);
    }

    private void OnLogReceived(TextLine message)
    {
        OnMessageReceived(new LogLineDeck()
        {
            Text = message.Text,
            TextColor = message.TextColor.ToMauiColor()
        });
    }

    private void OnMessageReceived(LogLineDeck message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogLines.Add(message);
            ScrollToLastRequested?.Invoke(this, EventArgs.Empty);
        });
    }
}
