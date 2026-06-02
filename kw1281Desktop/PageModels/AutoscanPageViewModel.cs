using BitFab.KW1281Test;
using BitFab.KW1281Test.Enums;
using System.Windows.Input;
using kw1281Desktop.PageModels.BasePageViewModels;
using BitFab.KW1281Test.Models;
using BitFab.KW1281Test.Actions;

namespace kw1281Desktop.PageModels;

public sealed partial class AutoscanPageViewModel(Diagnostic diagnostic, ILoaderService loader)
    : BaseScanViewPageModel(diagnostic, loader)
{
    public ICommand ReadCommand => new Command(async () =>
    {
        using var dataScope = DataSender.Instance.BeginScope();

        try
        {
            DataSender.Instance.DataReceived += OnResultReceived;

            await ExecuteReadInBackgroundWithLoader(
                0,
                Commands.AutoScan,
                args: [.. Addresses.Select(address => (Arg)address.Value)]);
        }
        finally
        {
            DataSender.Instance.DataReceived -= OnResultReceived;
        }
    });
}
