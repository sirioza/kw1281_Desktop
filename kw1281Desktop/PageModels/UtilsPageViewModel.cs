using BitFab.KW1281Test;
using BitFab.KW1281Test.Enums;
using CommunityToolkit.Maui.Extensions;
using kw1281Desktop.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using kw1281Desktop.Extensions;
using kw1281Desktop.PageModels.BasePageViewModels;
using BitFab.KW1281Test.Models;
using BitFab.KW1281Test.Actions;

namespace kw1281Desktop.PageModels;

public sealed class UtilsPageViewModel : BaseScanViewPageModel
{
    public ObservableCollection<ObservableAddressValuePair> AddressValuePairs { get; } = [];
    readonly ActuatorDialogPage _popup = new() { CanBeDismissedByTappingOutsideOfPopup = false };

    public UtilsPageViewModel(Diagnostic diagnostic, ILoaderService loader)
        : base(diagnostic, loader)
    {
        AddressValuePairs.Add(new ObservableAddressValuePair { IsVisible = true });
    }

    private Commands _selectedCommand;
    public Commands SelectedCommand
    {
        get => _selectedCommand;
        set => SetProperty(ref _selectedCommand, value);
    }

    private ElementItem<int>? _selectedAddress;
    public ElementItem<int> SelectedAddress
    {
        get => _selectedAddress ?? Addresses.First();
        set => SetProperty(ref _selectedAddress, value);
    }

    public CustomTuple CodeWorkshop{ get; } = new();

    private string _login = string.Empty;
    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public CustomTuple AddressValue { get; } = new();

    public ICommand RunCommand => new Command(async () =>
    {
        Arg[] args = null!;

        switch (SelectedCommand)
        {
            case Commands.SetSoftwareCoding:
                args = [CodeWorkshop.Value1, CodeWorkshop.Value2];
                break;
            case Commands.WriteEeprom:
                args = [AddressValue.Value1, AddressValue.Value2];
                break;
            case Commands.FindLogins:
                args = [Login];
                break;
            case Commands.WriteEdc15Eeprom:
                args = AddressValuePairs.FlattenPairs();
                break;
            case Commands.ActuatorTest:
                await RunActuatorLoopAsync();
                return;
        }

        using var dataScope = DataSender.Instance.BeginScope();

        try
        {
            DataSender.Instance.DataReceived += OnResultReceived;

            await base.ExecuteReadInBackgroundWithLoader(SelectedAddress.Value, SelectedCommand, args);
        }
        finally
        {
            DataSender.Instance.DataReceived -= OnResultReceived;
        }
    });

    public async Task RunActuatorLoopAsync()
    {
        var control = new ActuatorTestControl();

        _popup.Input = "Test is started...";
        _popup.NextClicked += control.RequestNext;
        _popup.CancelClicked += control.RequestStop;

        try
        {
            var popupTask = Shell.Current.ShowPopupAsync(_popup);
            using var dataScope = DataSender.Instance.BeginScope();

            try
            {
                DataSender.Instance.DataReceived += OnPopupResultReceived;

                await base.ExecuteReadInBackground(SelectedAddress.Value, SelectedCommand, control);
            }
            finally
            {
                DataSender.Instance.DataReceived -= OnPopupResultReceived;
            }

            await popupTask;
        }
        finally
        {
            _popup.NextClicked -= control.RequestNext;
            _popup.CancelClicked -= control.RequestStop;
        }
    }

    private void OnPopupResultReceived(IBaseResult baseResult)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (baseResult.Ok)
            {
                _popup.Input = baseResult.Content;
            }
            else if (!baseResult.Ok && baseResult is Result<Exception> ex)
            {
                _popup.Input = $"Error : {ex.Error.Message}";
            }
            else
            {
                _popup.Input = "Data result error.";
            }
        });
    }
}
