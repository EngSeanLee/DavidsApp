using DavidsApp.Client.ViewModels;

namespace DavidsApp.Client.Views;

[QueryProperty(nameof(ProjectId), "projectId")]
public partial class CapturePage : ContentPage
{
    private readonly CaptureViewModel _viewModel;

    public CapturePage(CaptureViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    public string ProjectId { get; set; } = string.Empty;

    // OnAppearing/OnDisappearing are `async void` — MAUI's page lifecycle leaves no alternative —
    // so an unhandled exception here is fatal to the whole process, not just this page. Both
    // CaptureViewModel methods already guard their own risky work internally (speech recognizer
    // startup failures in particular — see InitializeAsync), but this try/catch is defense in
    // depth against anything unanticipated, since the cost of getting this wrong is the entire
    // app crashing rather than one screen misbehaving.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (!string.IsNullOrEmpty(ProjectId))
            {
                await _viewModel.InitializeAsync(ProjectId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CapturePage.OnAppearing failed: {ex}");
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CapturePage.OnDisappearing failed: {ex}");
        }
    }
}
