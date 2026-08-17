using CommunityToolkit.Maui;
using DavidsApp.Client.Services;
using DavidsApp.Client.Services.Api;
using DavidsApp.Client.Services.Diagnostics;
using DavidsApp.Client.Services.Speech;
using DavidsApp.Client.Services.StateMachine;
using DavidsApp.Client.ViewModels;
using DavidsApp.Client.Views;
using Microsoft.Extensions.Logging;

#if ANDROID
using DavidsApp.Client.Platforms.Android.Speech;
#elif IOS
using DavidsApp.Client.Platforms.iOS.Speech;
#elif WINDOWS
using DavidsApp.Client.Platforms.Windows.Speech;
#endif

namespace DavidsApp.Client;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Defaults to tools/mock-api so a fresh checkout runs against something out of the box.
		// LocalConfigLoader overrides this from a gitignored Resources/Raw/appsettings.local.json
		// if one was bundled into this build — that's how a real deployment URL + shared secret
		// get in without ever touching source control (see docs/decisions/0002-auth-and-secrets.md).
		// One blocking async call at startup, before the UI/message loop exists — safe here, not
		// a pattern to repeat once the app is running.
		var apiClientOptions = LocalConfigLoader.LoadAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton(apiClientOptions);
		builder.Services.AddHttpClient<IApiClient, ApiClient>();
		builder.Services.AddTransient<CaptureStateMachine>();

		builder.Services.AddSingleton<ITextToSpeechService, MauiTextToSpeechService>();
		builder.Services.AddSingleton<IDiagnosticLog, FileDiagnosticLog>();
		builder.Services.AddSingleton<IUrlLauncher, MauiUrlLauncher>();
		builder.Services.AddSingleton<IPermissionRequester, MauiPermissionRequester>();
#if ANDROID
		builder.Services.AddSingleton<IContinuousSpeechRecognizer, AndroidContinuousSpeechRecognizer>();
#elif IOS
		builder.Services.AddSingleton<IContinuousSpeechRecognizer, IosContinuousSpeechRecognizer>();
#elif WINDOWS
		builder.Services.AddSingleton<IContinuousSpeechRecognizer, WindowsContinuousSpeechRecognizer>();
#else
		throw new PlatformNotSupportedException("No IContinuousSpeechRecognizer implementation for this target — only Android, iOS, and Windows are supported (see the build spec).");
#endif

		builder.Services.AddTransient<ProjectListViewModel>();
		builder.Services.AddTransient<CaptureViewModel>();
		builder.Services.AddTransient<ProjectListPage>();
		builder.Services.AddTransient<CapturePage>();

		return builder.Build();
	}
}
