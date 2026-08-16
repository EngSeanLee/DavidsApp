using CommunityToolkit.Maui;
using DavidsApp.Client.Services;
using DavidsApp.Client.Services.Api;
using DavidsApp.Client.Services.Speech;
using DavidsApp.Client.Services.StateMachine;
using DavidsApp.Client.ViewModels;
using DavidsApp.Client.Views;
using Microsoft.Extensions.Logging;

#if ANDROID
using DavidsApp.Client.Platforms.Android.Speech;
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

		// Phase 2 default: points at tools/mock-api so a fresh checkout runs against something
		// out of the box (see ApiClientOptions). Swapping to the real deployed Apps Script Web
		// App URL + shared secret is a later step — never hardcode those here (see
		// docs/decisions/0002-auth-and-secrets.md).
		builder.Services.AddSingleton(new ApiClientOptions());
		builder.Services.AddHttpClient<IApiClient, ApiClient>();
		builder.Services.AddTransient<CaptureStateMachine>();

		builder.Services.AddSingleton<ITextToSpeechService, MauiTextToSpeechService>();
#if ANDROID
		builder.Services.AddSingleton<IContinuousSpeechRecognizer, AndroidContinuousSpeechRecognizer>();
#elif WINDOWS
		builder.Services.AddSingleton<IContinuousSpeechRecognizer, WindowsContinuousSpeechRecognizer>();
#else
		throw new PlatformNotSupportedException("No IContinuousSpeechRecognizer implementation for this target — only Android and Windows are supported (see the build spec).");
#endif

		builder.Services.AddTransient<ProjectListViewModel>();
		builder.Services.AddTransient<CaptureViewModel>();
		builder.Services.AddTransient<ProjectListPage>();
		builder.Services.AddTransient<CapturePage>();

		return builder.Build();
	}
}
