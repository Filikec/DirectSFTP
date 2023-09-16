using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace DirectSFTP;

// I changed Project File and launchSettings to make it unpackaged
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

		Task.Run(() => { SFTP.GetInstance().Connect("46.13.164.29", 50001, "bandaska", "raketaletadlo"); });

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
