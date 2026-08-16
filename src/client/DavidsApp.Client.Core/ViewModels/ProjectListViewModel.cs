using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DavidsApp.Client.Models;
using DavidsApp.Client.Services.Api;
using Microsoft.Extensions.Logging;

namespace DavidsApp.Client.ViewModels;

/// <summary>Project select/start screen — spec §"Idle: project selected, ready to capture" starts here.</summary>
public sealed partial class ProjectListViewModel : ObservableObject
{
    private readonly IApiClient _api;
    private readonly ILogger<ProjectListViewModel> _logger;

    public ProjectListViewModel(IApiClient api, ILogger<ProjectListViewModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    public ObservableCollection<Project> Projects { get; } = new();

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTestingAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewJobNumber { get; set; } = string.Empty;

    /// <summary>Raised when StartProject/SelectProject succeeds — the view navigates to CapturePage on this, rather than polling a property.</summary>
    public event EventHandler<Project>? ProjectActivated;

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var envelope = await _api.ListProjectsAsync();
            Projects.Clear();
            if (envelope.Status == Models.ApiStatus.Confirm && envelope.Data is not null)
            {
                foreach (var project in envelope.Data.Projects)
                {
                    Projects.Add(project);
                }
            }
            else
            {
                ErrorMessage = envelope.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load projects");
            ErrorMessage = "Couldn't load projects.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTestingAddress))
        {
            ErrorMessage = "Testing address is required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var envelope = await _api.StartProjectAsync(NewTestingAddress, testingDate: null, NewJobNumber);
            if (envelope.Status == Models.ApiStatus.Confirm && envelope.Data is not null)
            {
                NewTestingAddress = string.Empty;
                NewJobNumber = string.Empty;
                ProjectActivated?.Invoke(this, envelope.Data);
            }
            else
            {
                ErrorMessage = envelope.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start project");
            ErrorMessage = "Couldn't start the project.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectProject(Project project)
    {
        ProjectActivated?.Invoke(this, project);
    }
}
