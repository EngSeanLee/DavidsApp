using DavidsApp.Client.ViewModels;

namespace DavidsApp.Client.Views;

public partial class ProjectListPage : ContentPage
{
    private readonly ProjectListViewModel _viewModel;

    public ProjectListPage(ProjectListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _viewModel.ProjectActivated += OnProjectActivated;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadProjectsCommand.ExecuteAsync(null);
    }

    // async void (Shell/event-handler constraint) — an unhandled exception here is fatal to the
    // whole process, so this is guarded the same way as CapturePage's lifecycle methods.
    private async void OnProjectActivated(object? sender, Models.Project project)
    {
        try
        {
            await Shell.Current.GoToAsync($"{nameof(CapturePage)}?projectId={Uri.EscapeDataString(project.ProjectId)}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation to CapturePage failed: {ex}");
        }
    }
}
