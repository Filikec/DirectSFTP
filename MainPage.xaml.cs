using System.Collections.ObjectModel;
using System.Diagnostics;
using WinSCP;

namespace DirectSFTP;

public partial class MainPage : ContentPage
{
	private ObservableCollection<DirectoryElementInfo> dirElements = new();
    private ObservableCollection<TransferInfo> transfers = new();
    private SFTP sftp;

	public MainPage()
	{
		InitializeComponent();
		dirView.ItemsSource = dirElements;
		transView.ItemsSource = transfers;
		dirView.Scrolled += (a, b) =>
		{
			for (int i = b.FirstVisibleItemIndex; i <= b.LastVisibleItemIndex; i++)
			{
				if (i < dirElements.Count && i>=0) dirElements[i].UpdatedImg();
			}
		};
		sftp = SFTP.GetInstance();
		Task.Run(()=>UpdateDir(SFTP.CurDir));
	}

	private void OnParentFolderClick(object sender, EventArgs e)
	{
		string path = SFTP.CurDir;

		if (path == "/") return;

		path = SFTP.RemoteGetDirName(path);

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
        await Dispatcher.DispatchAsync(()=>Title = dir);
        dirElements.Clear();
		string thumbFolder = SFTP.GetThumbnailFolder(dir);

		foreach (RemoteFileInfo file in files.Files)
		{
			if (file.Name == ".dthumb")
			{
				string tmpFolder = thumbFolder;
				if (!Directory.Exists(tmpFolder)) Directory.CreateDirectory(tmpFolder);
				sftp.EnqueueFolderDownload(file.FullName, tmpFolder, true);
            }
			if (file.Name.StartsWith('.')) continue;

			if (file.IsDirectory == false) Debug.WriteLine(Path.Join(thumbFolder, file.Name)+ " < img name");
			dirElements.Add(new()
			{
				FileInfo = file,
				ImagePath = file.IsDirectory ? "" : Path.Join(thumbFolder, file.Name),
				OnDownload = new Command(() =>
				{
					Debug.WriteLine("Clicked " + file.FullName);
					if (file.IsDirectory) EnqueueFolderDownload(file.FullName,"D:\\Downloads");
					else EnqueueFileDownload(file.FullName, "D:\\Downloads");
				}),
				OnClick = new Command(() =>
				{
                    Debug.WriteLine("Clicked " + file.FullName + " download");
                    if (file.IsDirectory) UpdateDir(file.FullName);
                    else EnqueueFileDownload(file.FullName, "D:\\Downloads");
                })

			}); ;
		}
	}


    public void CreateTransferButton(TransferInfo transfer)
    {
        transfer.OnCancel = new Command(() => {
            sftp.CancelTransfer(transfer.Id);
            if (SFTP.curTrans.Id != transfer.Id)
            {
                transfers.Remove(transfer);
            }

        });
        transfers.Add(transfer);

        sftp.TransferEvents[transfer.Id] += (object a, Tuple<int, TransferEventType> b) =>
        {
            TransferInfo info = (TransferInfo)a;
            if (b.Item2 == TransferEventType.Cancelled)
            {
                transfers.Remove(transfer);
            }
            else if (b.Item2 == TransferEventType.Progress)
            {
                transfer.UpdateProgress();
            }
            else if (b.Item2 == TransferEventType.Finished)
            {
                transfers.Remove(transfer);
            }

        };
    }
    public void EnqueueFileDownload(string filePath, string target){
		TransferInfo transfer = sftp.EnqueueFileDownload(filePath, target);
        CreateTransferButton(transfer);
	}

	public void EnqueueFolderDownload(string filePath, string target, bool insideFolder=true)
	{
		if (insideFolder)
		{
			target = Path.Join(target,SFTP.RemoteGetFileName(filePath));
		}
        TransferInfo transfer = sftp.EnqueueFolderDownload(filePath, target);
        CreateTransferButton(transfer);
    }
}

