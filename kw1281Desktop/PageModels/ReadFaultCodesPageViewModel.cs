using BitFab.KW1281Test;
using BitFab.KW1281Test.Enums;
using kw1281Desktop.Models;
using System.Windows.Input;
using kw1281Desktop.PageModels.BasePageViewModels;
using BitFab.KW1281Test.Actions;

namespace kw1281Desktop.PageModels;

public sealed partial class ReadFaultCodesPageViewModel(Diagnostic diagnostic, ILoaderService loader)
    : BaseScanViewPageModel(diagnostic, loader)
{
    private ElementItem<int>? _selectedAddress;
    public ElementItem<int> SelectedAddress
    {
        get => _selectedAddress ?? Addresses.First();
        set => SetProperty(ref _selectedAddress, value);
    }

    public ICommand ReadClearCommand => new Command(async command =>
    {
        using var dataScope = DataSender.Instance.BeginScope();

        try
        {
            DataSender.Instance.DataReceived += OnResultReceived;

            await ExecuteReadInBackgroundWithLoader(SelectedAddress.Value, Enum.Parse<Commands>(command.ToString()!));
        }
        finally
        {
            DataSender.Instance.DataReceived -= OnResultReceived;
        }
    });

    public ICommand GoToGroupCommand => new Command(async () =>
    {
        await Shell.Current.GoToAsync($"{nameof(GroupReadPage)}?address={SelectedAddress.Value}");
    });
}
