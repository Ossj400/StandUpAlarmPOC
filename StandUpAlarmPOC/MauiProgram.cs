using Fonts;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using StandUpAlarmPOC.Interfaces;
#if ANDROID
using StandUpAlarmPOC.Platforms.Android.Services;
#endif

namespace StandUpAlarmPOC;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
            });

#if DEBUG
        builder.Logging.AddDebug();
        builder.Services.AddLogging(configure => configure.AddDebug());
#endif
        builder.Services.AddSingleton<IVibration>(Vibration.Default);
        builder.Services.AddSingleton<IDispatcher>(provider =>
            Application.Current.Dispatcher);
        builder.ConfigureLifecycleEvents(events => {
#if ANDROID
        events.AddAndroid(android => android
            .OnCreate((activity, bundle) => Platform.Init(activity, bundle)));
#endif
        });
#if ANDROID
       // builder.UseMauiApp<App>().UseMauiCommunityToolkit();

        builder.Services.AddSingleton<IImageProcessing, ImageProcessingService>();
        builder.Services.AddSingleton<ICamera>(sp => 
    new AndroidCameraService());


#endif

#if WINDOWS


#endif
        return builder.Build();
	}
}
