using BitFab.KW1281Test;
using BitFab.KW1281Test.Actions;
using BitFab.KW1281Test.Enums;
using BitFab.KW1281Test.Models;
using kw1281Desktop.Models;
using kw1281Desktop.PageModels.BasePageViewModels;
using System.Collections.ObjectModel;

namespace kw1281Desktop.PageModels;

[QueryProperty(nameof(Address), "address")]
public sealed partial class GroupReadPageViewModel : BaseScanViewPageModel
{
    public GroupReadPageViewModel(Diagnostic diagnostic, ILoaderService loader)
        : base(diagnostic, loader)
    {
        InitRows();
    }

    public ObservableCollection<GroupRow> Rows { get; } = [];

    private int _address;
    public int Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    private bool _isBasicSetting;
    public bool IsBasicSetting
    {
        get => _isBasicSetting;
        set => SetProperty(ref _isBasicSetting, value);
    }

    private async Task ExecuteStart(GroupRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Input))
        {
            return;
        }

        await base.ExecuteReadInBackgroundWithLoader(
            Address,
            !IsBasicSetting ? Commands.GroupRead : Commands.BasicSetting,
            row.Id.ToString());
    }

    protected override async Task ExecuteReadInBackground(int controllerAddress, Commands command, params Arg[] args)
    {
        GroupRow row = Rows.First(row => row.Id.Equals(Guid.Parse(args[0].Get<string>())));
        var groupInput = row.Input!;

        TaskCompletionSource<IBaseResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var dataScope = DataSender.Instance.BeginScope();

        Action<IBaseResult> handler = null!;

        handler = (result) =>
        {
            DataSender.Instance.DataReceived -= handler;
            tcs.TrySetResult(result);
        };

        DataSender.Instance.DataReceived += handler;

        try
        {
            await Task.Run(() => Diagnostic.RunAsync(
                AppSettings.Port!,
                AppSettings.Baud,
                controllerAddress,
                command,
                (Arg)groupInput));

            tcs.TrySetResult(new Result<Exception>(default!, new Exception("No data received.")));
        }
        finally
        {
            DataSender.Instance.DataReceived -= handler;
        }

        IBaseResult result = await tcs.Task;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            row.Fields.Clear();

            if (result.Ok && result is Result<List<KeyValuePair<byte, string>>> group)
            {
                int groupsCount = group.Data.Count;
                for (int i = 0; i < (groupsCount < 4 ? 4 : groupsCount); i++)
                {
                    if (i < groupsCount)
                    {
                        row.Fields.Add(new FieldItem
                        {
                            Key = groupsCount <= 4 ? group.Data[i].Key : null,
                            Value = group.Data[i].Value,
                            Whidth = 800 / groupsCount
                        });
                    }
                    else
                    {
                        row.Fields.Add(new FieldItem { Key = 0, Value = "Not available" });
                    }
                }
            }
            else if (!result.Ok && result is Result<Exception> ex)
            {
                row.Fields.Add(new FieldItem { Value = $"Error : {ex.Error.Message}", Whidth = 800 });
            }
        });
    }

    private void InitRows()
    {
        for (int i = 0; i < 5; i++)
        {
            var row = new GroupRow
            {
                Input = i.ToString()
            };
            row.StartCommand = new Command(async () => await ExecuteStart(row));
            Rows.Add(row);
        }
    }
}
