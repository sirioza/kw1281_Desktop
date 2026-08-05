namespace kw1281Desktop.Pages;

public partial class ActuatorDialogPage : CommunityToolkit.Maui.Views.Popup
{
    public event Action? NextClicked;
    public event Action? CancelClicked;

    public ActuatorDialogPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    void OnNext(object sender, EventArgs e) => NextClicked?.Invoke();

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync();
        CancelClicked?.Invoke();
    }

    private string? _input;
    public string? Input
    {
        get => _input;
        set
        {
            if (_input == value) return;
            _input = value;
            OnPropertyChanged(nameof(Input));
        }
    }
}