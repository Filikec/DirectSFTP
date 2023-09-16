using System.Collections.ObjectModel;
using System.Diagnostics;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace DirectSFTP;

public partial class ConnectPage : ContentPage
{
    private ObservableCollection<DirectoryElementInfo> dirElements = new();
    private ObservableCollection<TransferInfo> transfers = new();
    private SFTP sftp;

    public ConnectPage()
    {
        InitializeComponent();

        dirView.ItemsSource = dirElements;
        transView.ItemsSource = transfers;
        dirView.Scrolled += (a, b) =>
        {
            for (int i = b.FirstVisibleItemIndex; i <= b.LastVisibleItemIndex; i++)
            {
                if (i < dirElements.Count && i >= 0 && dirElements[i].ImgUpdated==false)
                {
                    var fileInfo = dirElements[i];
                    if (fileInfo.FileInfo.IsDirectory==false && fileInfo.Updating==false) DownloadThumbnail(fileInfo);
                }
            }
        };
        sftp = SFTP.GetInstance();

        if (SFTP.IsConnected) Task.Run(() => { UpdateDir(SFTP.CurDir); });
        else SFTP.Connected += (a, b) => Task.Run(() => { UpdateDir(SFTP.CurDir); });

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
        IEnumerable<SftpFile> files;
        try
        {
            files = await sftp.ListDir(dir);
        }
        catch (SftpPermissionDeniedException)
        {
            await DisplayAlert("Alert", "You lack permissions", "OK");
            return;
        }catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return;
        }

        SFTP.CurDir = dir;
        await Dispatcher.DispatchAsync(() => Title = dir);
        dirElements.Clear();
        string thumbFolder = SFTP.GetThumbnailFolder(dir);

        int i = 0;
        foreach (var file in files)
        {
            if (file.Name.StartsWith('.')) continue;
            
            dirElements.Add(new()
            {
                FileInfo = file,
                OnDownload = new Command(() =>
                {
                    if (file.IsDirectory) EnqueueFolderDownload(file.FullName, SFTP.GetDownloadFolder());
                    else EnqueueFileDownload(file.FullName, SFTP.GetDownloadFolder());
                }),
                OnClick = new Command(() =>
                {
                    if (file.IsDirectory) UpdateDir(file.FullName);
                    else EnqueueFileDownload(file.FullName, SFTP.GetDownloadFolder());
                })

            });

            if (file.IsDirectory==false && i < 6 && dirElements.Last().Updating == false)
            {
                DownloadThumbnail(dirElements.Last());
            }

            i++;
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
            else if (b.Item2 == TransferEventType.Error)
            {
                transfers.Remove(transfer);
            }
            else if (b.Item2 != TransferEventType.CalculatingDistances)
            {
                transfer.UpdateProgress();
            }

        };
    }
    public void EnqueueFileDownload(string filePath, string target, bool thumbnail=false)
    {
        TransferInfo transfer = sftp.EnqueueFileDownload(filePath, target, thumbnail);
        CreateTransferButton(transfer);
    }

    public void EnqueueFolderDownload(string filePath, string target)
    {
        TransferInfo transfer = sftp.EnqueueFolderDownload(filePath, target);
        CreateTransferButton(transfer);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.CurrentSelection)
        {
            Debug.WriteLine(item.ToString());
        }
        
    }

    private async void DownloadThumbnail(DirectoryElementInfo file)
    {
        file.Updating = true;

        string remotePath = SFTP.RemoteJoinPath(SFTP.RemoteGetDirName(file.FileInfo.FullName),".dthumb");
        remotePath = SFTP.RemoteJoinPath(remotePath, file.FileInfo.Name);

        string thumbFolder = SFTP.GetThumbnailFolder(SFTP.RemoteGetDirName(file.FileInfo.FullName));

        if (await Task.Run(() => sftp.GetSession().Exists(remotePath))==false){
            return;
        }

        if (!Directory.Exists(thumbFolder)) Directory.CreateDirectory(thumbFolder);

        

        var transfer = sftp.EnqueueFileDownload(remotePath, thumbFolder,true);
        sftp.TransferEvents[transfer.Id] += (object a, Tuple<int, TransferEventType> b) =>
        {
            TransferInfo info = (TransferInfo)a;
            if (b.Item2 == TransferEventType.Finished)
            {
                file.ImagePath = Path.Join(thumbFolder, file.FileInfo.Name);
                file.UpdatedImg();
            }
        };
    }
}