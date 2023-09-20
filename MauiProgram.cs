using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Microsoft.Maui.LifecycleEvents;
using System.Diagnostics;

namespace DirectSFTP;

// I changed Project File and launchSettings to make it unpackaged
public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
        GlobalHooks.StartHooks();
        var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			}).ConfigureLifecycleEvents(e =>
            {
#if ANDROID
				e.AddAndroid(android => android.OnActivityResult((act, req, res, intent) =>
                {   
                    if (req == 70){
						var folderPath = ToPhysicalPath(intent.Data);
                        Preferences.Default.Set("DownloadFolder",folderPath);
                        Debug.WriteLine(SFTP.GetDownloadFolder());
                    }
                }));
#endif
            });
        
#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
#if ANDROID
    public static string ToPhysicalPath(Android.Net.Uri uri)
    {
        const string uriSchemeFolder = "content";
        if (uri.Scheme is null || !uri.Scheme.Equals(uriSchemeFolder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.PathSegments?.Count < 2)
        {
            return null;
        }

        // Example path would be /tree/primary:DCIM, or /tree/SDCare:DCIM
        var path = uri.PathSegments?[1];

        if (path is null)
        {
            return null;
        }

        var pathSplit = path.Split(':');
        if (pathSplit.Length < 2)
        {
            return null;
        }
        // Primary is the device's internal storage, and anything else is an SD card or other external storage
        if (pathSplit[0].Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            // Example for internal path /storage/emulated/0/DCIM
            return $"{Android.OS.Environment.ExternalStorageDirectory?.Path}/{pathSplit[1]}";
        }

        // Example for external path /storage/1B0B-0B1C/DCIM
        return $"/{"storage"}/{pathSplit[0]}/{pathSplit[1]}";
    }

    /// <summary>
    /// Get External Directory Path
    /// </summary>
    public static string GetExternalDirectory()
    {
        return Android.OS.Environment.ExternalStorageDirectory?.Path ?? "/storage/emulated/0";
    }
#endif
}
