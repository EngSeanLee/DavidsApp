using DavidsApp.Client.Views;

namespace DavidsApp.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(CapturePage), typeof(CapturePage));
    }
}
