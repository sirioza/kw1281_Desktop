using kw1281Desktop.Models.Base;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace kw1281Desktop.Models;

public partial class GroupRow : BasePropertyChanged
{
    public GroupRow()
    {
        Id = Guid.NewGuid();
    }

    public readonly Guid Id;

    private string? _input;
    public string? Input
    {
        get => _input;
        set => SetProperty(ref _input, value);
    }

    public ObservableCollection<FieldItem> Fields { get; } =
    [
        new FieldItem { Key = 0, Value = string.Empty },
        new FieldItem { Key = 0, Value = string.Empty },
        new FieldItem { Key = 0, Value = string.Empty },
        new FieldItem { Key = 0, Value = string.Empty }
    ];

    public ICommand? StartCommand { get; set; }
}

public class FieldItem
{
    public byte? Key { get; set; }
    public string? Value { get; set; }
    public int Whidth { get; set; } = 200;
}
