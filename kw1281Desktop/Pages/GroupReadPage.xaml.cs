namespace kw1281Desktop.Pages;

public partial class GroupReadPage : ContentPage
{
    public GroupReadPage(GroupReadPageViewModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}