using AgentYattus.Client.Core;

namespace AgentYattus.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
