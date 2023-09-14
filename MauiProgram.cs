using Microsoft.Extensions.Logging;

namespace DirectSFTP;

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
			});

		Task.Run(() => { SFTP.GetInstance().Connect("46.13.164.29", 50001, "bandaska", "raketaletadlo"); });

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
