using kw1281Desktop.Models.Base;

namespace kw1281Desktop.Models;

public partial class CustomTuple : BasePropertyChanged
{
    private string _value1 = string.Empty;
    public string Value1
    {
        get => _value1;
        set => SetProperty(ref _value1, value);
    }

    private string _value2 = string.Empty;
    public string Value2
    {
        get => _value2;
        set => SetProperty(ref _value2, value);
    }
}