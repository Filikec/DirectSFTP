using System.Collections.ObjectModel;
using System.Diagnostics;
using WinSCP;

namespace DirectSFTP;

public partial class MainPage : ContentPage
{
	public ObservableCollection<DirectoryElementInfo> dirElements = new();
	private SFTP sftp;

	public MainPage()
	{
		InitializeComponent();
		collectionView.ItemsSource = dirElements;
		sftp = SFTP.GetInstance();
		Task.Run(()=>UpdateDir(SFTP.CurDir));
	}

	private void OnParentFolderClick(object sender, EventArgs e)
	{
		string path = SFTP.CurDir;


		if (path == "/") return;


		UpdateDir(path);
	}

	private async void UpdateDir(string dir)
	{
		RemoteDirectoryInfo files;
		try
		{
            files = await sftp.ListDir(dir);
        }catch (Exception ex)
		{
			Debug.WriteLine(ex.ToString());
			return;
		}

		SFTP.CurDir = dir;
        await MainThread.InvokeOnMainThreadAsync(()=>Title = dir);

        dirElements.Clear();
		foreach (RemoteFileInfo file in files.Files)
		{
			if (file.Name.StartsWith('.')) continue;

			dirElements.Add(new()
			{
				FileInfo = file,
				ImagePath = "",
				OnClick = new Command(() => { 
					Debug.WriteLine("Clicked " + file.FullName);
					if (file.IsDirectory)
					{
						UpdateDir(file.FullName);
                    }
				})
			});
		}
	}
}

