using BitFab.KW1281Test;
using BitFab.KW1281Test.Enums;
using kw1281Desktop.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using kw1281Desktop.PageModels.BasePageViewModels;
using WindowsAPICodePack.Dialogs;
using BitFab.KW1281Test.Models;
using BitFab.KW1281Test.Actions;

namespace kw1281Desktop.PageModels;

public sealed partial class DumpPageViewModel(Diagnostic diagnostic, ILoaderService loader)
    : BaseScanViewPageModel(diagnostic, loader)
{
    private ElementItem<int>? _selectedAddress;
    public ElementItem<int> SelectedAddress
    {
        get => _selectedAddress ?? Addresses.First();
        set => SetProperty(ref _selectedAddress, value);
    }

    public ObservableCollection<DumpItem> DumpCommands { get; } =
    [
        new(Commands.DumpEeprom, "Dump Eeprom", new("0", true), new("2048", true)),
        new(Commands.DumpEdc15Eeprom, "Dump Edc15Eeprom"),
        new(Commands.DumpMarelliMem, "Dump Marelli Mem", new("3072", true), new("1024", true)),
        new(Commands.DumpMem, "Dump Mem", new("8192", true), new("65536", true)),
        new(Commands.DumpRam, "Dump Ram", new("8192", true), new("65536", true)),
        new(Commands.DumpRom, "Dump Rom", new("8192", true), new("65536", true)),
        new(Commands.DumpRBxMem, "Dump RBxMem", new ("66560", true), new("1024", true)),
        new(Commands.DumpRBxMemOdd, "Dump RBxMemOdd"),
        new(Commands.DumpCcmRom, "Dump CcmRom"),
        new(Commands.DumpClusterNecRom, "Dump RBxMemOdd"),
        new(Commands.ReadEeprom, "Read Eeprom", new("4361", true), new(null!, false)),
        new(Commands.ReadRAM, "Read RAM", new("4361", true), new(null!, false)),
        new(Commands.ReadROM, "Read ROM", new("4361", true), new(null!, false)),
        new(Commands.LoadEeprom, "Load Eeprom", new ("0", true), new(null!, false)),
        new(Commands.MapEeprom, "Map Eeprom")
    ];

    private DumpItem? _previousSelectedDump;

    private DumpItem? _selectedDump;
    public DumpItem SelectedDump
    {
        get => _selectedDump ?? DumpCommands.First();
        set
        {
            SetProperty(ref _selectedDump, value);
            ChangePropertiesState(value);
        }
    }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    private string _start = string.Empty;
    public string Start
    {
        get => _start;
        set => SetProperty(ref _start, value);
    }


    private string _length = string.Empty;
    public string Length
    {
        get => _length;
        set => SetProperty(ref _length, value);
    }

    private bool _isStartFieldEnabled;
    public bool IsStartFieldEnabled
    {
        get => _isStartFieldEnabled;
        set => SetProperty(ref _isStartFieldEnabled, value);
    }

    private bool _isLengthFieldEnabled;
    public bool IsLengthFieldEnabled
    {
        get => _isLengthFieldEnabled;
        set => SetProperty(ref _isLengthFieldEnabled, value);
    }

    public ICommand ReadCommand => new Command(async () =>
    {
        var selectedDump = SelectedDump;
        List<(string, bool)> parameters = [
             (Start, selectedDump.Start.Item2),
            (Length, selectedDump.Length.Item2),
            (FilePath, true)];

        Arg[] args = [.. parameters.Where(arg => arg.Item2).Select(arg => (Arg)arg.Item1)];
        using var dataScope = DataSender.Instance.BeginScope();

        try
        {
            DataSender.Instance.DataReceived += OnResultReceived;

            await ExecuteReadInBackgroundWithLoader(SelectedAddress.Value, selectedDump.Value, args);
        }
        finally
        {
            DataSender.Instance.DataReceived -= OnResultReceived;
        }
    });

    public ICommand ResetCommand => new Command(async () =>
    {
        await ExecuteReadInBackgroundWithLoader(SelectedAddress.Value, Commands.Reset);
    });

    public ICommand ChooseCommand => new Command(() =>
    {
        bool isFolderPicker = !SelectedDump.Value.Equals(Commands.LoadEeprom);

        using CommonOpenFileDialog dialog = new() { IsFolderPicker = isFolderPicker };

        if (!isFolderPicker)
        {
            dialog.Filters.Add(new("Dump", "*.bin"));
        }

        CommonFileDialogResult result = dialog.ShowDialog();
        if (result == CommonFileDialogResult.Ok)
        {
            FilePath = dialog.FileName;
        }
    });

    private void ChangePropertiesState(DumpItem value)
    {
        bool wasSpecial = _previousSelectedDump?.Value == Commands.LoadEeprom;
        bool nowSpecial = value.Value == Commands.LoadEeprom;

        if (wasSpecial != nowSpecial)
        {
            FilePath = string.Empty;
        }

        _previousSelectedDump = value;

        IsLengthFieldEnabled = value.Length.Item2;
        IsStartFieldEnabled = value.Start.Item2;
        Start = value.Start.Item1 ?? string.Empty;
        Length = value.Length.Item1 ?? string.Empty;
    }
}
