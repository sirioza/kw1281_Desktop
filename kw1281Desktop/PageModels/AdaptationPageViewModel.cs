using BitFab.KW1281Test;
using BitFab.KW1281Test.Enums;
using kw1281Desktop.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using kw1281Desktop.PageModels.BasePageViewModels;
using BitFab.KW1281Test.Actions;

namespace kw1281Desktop.PageModels;

public sealed partial class AdaptationPageViewModel(Diagnostic diagnostic, ILoaderService loader)
    : BaseScanViewPageModel(diagnostic, loader)
{
    public ObservableCollection<ElementItem<Commands>> CommandList { get; } =
    [
        new( Commands.AdaptationRead, "Read" ),
        new( Commands.AdaptationTest, "Test" ),
        new( Commands.AdaptationSave, "Save" )
    ];

    private ElementItem<Commands>? _selectedCommand;
    public ElementItem<Commands> SelectedCommand
    {
        get => _selectedCommand ?? CommandList.First();
        set
        {
            SetProperty(ref _selectedCommand, value);

            if (value.Value.Equals(Commands.AdaptationRead))
            {
                IsFieldEnabled = false;
            }
            else
            {
                IsFieldEnabled = true;
            }
        }
    }

    private ElementItem<int>? _selectedAddress;
    public ElementItem<int> SelectedAddress
    {
        get => _selectedAddress ?? Addresses.First();
        set => SetProperty(ref _selectedAddress, value);
    }

    private string _сhannel = string.Empty;
    public string Channel
    {
        get => _сhannel;
        set => SetProperty(ref _сhannel, value);
    }

    private string _value = string.Empty;
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    private string _login = string.Empty;
    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    private bool _isFieldEnabled;
    public bool IsFieldEnabled
    {
        get => _isFieldEnabled;
        set => SetProperty(ref _isFieldEnabled, value);
    }

    public ICommand RunCommand => new Command(async () =>
    {
        var command = SelectedCommand.Value;
        using var dataScope = DataSender.Instance.BeginScope();

        try
        {
            DataSender.Instance.DataReceived += OnResultReceived;

            await ExecuteReadInBackgroundWithLoader(
                SelectedAddress.Value,
                command,
                args: command.Equals(Commands.AdaptationRead) ? [Channel, Login] : [Channel, Value, Login]);
        }
        finally
        {
            DataSender.Instance.DataReceived -= OnResultReceived;
        }
    });
}
