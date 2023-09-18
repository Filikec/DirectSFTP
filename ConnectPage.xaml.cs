using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

using SharpHook;

namespace DirectSFTP;

public partial class ConnectPage : ContentPage
{
    private ObservableCollection<DirectoryElementInfo> dirElements = new();
    private ObservableCollection<TransferInfo> transfers = new();
    private SFTP sftp;
    private SelectedOptions selectedOptions;
    private Stack<string> prevPaths;

    public ConnectPage()
    {
        InitializeComponent();

        prevPaths = new();

        // adds special hooks for input for windows
        AddHooks();

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

        Command onClear = new Command(() => Dispatcher.Dispatch(()=>dirView.SelectedItems.Clear()));

        selectedOptions = new(this,selectionStack,onClear);
    }



    private void OnParentFolderClick(object sender, EventArgs e)
    {
        string path = SFTP.CurDir;
        
        if (path == "/") return;

        prevPaths.Push(path);

        path = SFTP.RemoteGetDirName(path);

        UpdateDir(path);
    }

    private async void OnUpload(object sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickMultipleAsync();
        foreach( var file in result)
        {
            EnqueueFileUpload(file.FullPath, SFTP.CurDir);
        }
        Debug.WriteLine(e.ToString());
    }
    private void OnReload(object sender, EventArgs e)
    {
        UpdateDir(SFTP.CurDir);
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
            Debug.WriteLine(ex.ToString() + " <<<");
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
                OnClick = new Command(() =>
                {
                    if (file.IsDirectory){
                        prevPaths.Clear();
                        UpdateDir(file.FullName);
                    }
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
        Debug.WriteLine("Creating Transfer button");
        transfer.OnCancel = new Command(() => {
            sftp.CancelTransfer(transfer);
            if (SFTP.curTrans != null && SFTP.curTrans.Id != transfer.Id)
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
                Debug.WriteLine("Removing transfer " + "Cancelled");
                transfers.Remove(transfer);
            }
            else if (b.Item2 == TransferEventType.Progress)
            {
                transfer.UpdateProgress();
            }
            else if (b.Item2 == TransferEventType.Finished)
            {
                Debug.WriteLine("Removing transfer " + " Finished");
                transfers.Remove(transfer);
            }
            else if (b.Item2 == TransferEventType.Error)
            {
                Debug.WriteLine("Removing transfer " + "Error");
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

    public void EnqueueFileUpload(string filePath, string target)
    {
        TransferInfo transfer = sftp.EnqueueFileUpload(filePath, target);
        CreateTransferButton(transfer);
    }

    public void EnqueueFolderDownload(string filePath, string target)
    {
        TransferInfo transfer = sftp.EnqueueFolderDownload(filePath, target);
        CreateTransferButton(transfer);
    }


    private async void DownloadThumbnail(DirectoryElementInfo file)
    {
        file.Updating = true;

        string remotePath = SFTP.RemoteJoinPath(SFTP.RemoteGetDirName(file.FileInfo.FullName),".dthumb");
        remotePath = SFTP.RemoteJoinPath(remotePath, file.FileInfo.Name);

        string thumbFolder = SFTP.GetThumbnailFolder(SFTP.RemoteGetDirName(file.FileInfo.FullName));

        var isFile = await Task.Run(() =>
        {
            return sftp.GetSessionList().Exists(remotePath);
        });

        if (isFile == false) return;

        if (!Directory.Exists(thumbFolder)) Directory.CreateDirectory(thumbFolder);

        var transfer = sftp.EnqueueFileDownload(remotePath, thumbFolder, true);
        sftp.TransferEvents[transfer.Id] += (object a, Tuple<int, TransferEventType> b) =>
        {
            TransferInfo info = (TransferInfo) a;
            if (b.Item2 == TransferEventType.Finished)
            {
                file.ImagePath = Path.Join(thumbFolder, file.FileInfo.Name);
                file.UpdatedImg();
            }
        };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        
        foreach (DirectoryElementInfo item in e.PreviousSelection)
        {
            item.Selected = false;
        }

        foreach (DirectoryElementInfo item in e.CurrentSelection)
        {
            item.Selected = true;
        }
        

        if (e.CurrentSelection.Count > 0)
        {

            if (selectedOptions.IsShowing == false)
            {
                selectedOptions.ShowItems();
            }
            SetSelectionOnDownload(e.CurrentSelection);
            SetSelectionOnDelete(e.CurrentSelection);

        }else if (e.CurrentSelection.Count == 0 && selectedOptions.IsShowing)
        {
            selectedOptions.HideItems();
        }
    }

    private void SetSelectionOnDownload(IReadOnlyList<object> items)
    {
        selectedOptions.SetOnDownload(new Command(() =>
        {
            foreach (DirectoryElementInfo item in items)
            {
                if (item.FileInfo.IsDirectory)
                {
                    EnqueueFolderDownload(item.FileInfo.FullName, SFTP.GetDownloadFolder());
                }
                else
                {
                    EnqueueFileDownload(item.FileInfo.FullName, SFTP.GetDownloadFolder());
                }
            }
        }));
    }

    private void SetSelectionOnDelete(IReadOnlyList<object> items)
    {
        selectedOptions.SetOnDelete(new Command(() =>
        {
            Task.Run(() =>
            {
                foreach (DirectoryElementInfo item in items)
                {
                    try
                    {
                        if (item.FileInfo.IsDirectory) sftp.DeleteDirectory(item.FileInfo.FullName);
                        else sftp.DeleteFile(item.FileInfo.FullName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString() + "Whattt");
                    }
                }
                UpdateDir(SFTP.CurDir);
            });
        }));
    }

    private void AddHooks()
    {
        var hook = new TaskPoolGlobalHook();
        hook.KeyPressed += (a, b) =>
        {
            if (b.Data.KeyCode == SharpHook.Native.KeyCode.VcEscape && dirView.SelectedItems.Count > 0)
            {
                Dispatcher.Dispatch(()=> dirView.SelectedItems.Clear());
            }
        };
        hook.MousePressed += (a, b) =>
        {
            if (b.Data.Button == SharpHook.Native.MouseButton.Button4) OnParentFolderClick(null, null);
            else if (b.Data.Button == SharpHook.Native.MouseButton.Button5 && prevPaths.Count > 0)
            {
                UpdateDir(prevPaths.First());
                prevPaths.Pop();
            }


        };

        hook.RunAsync();
    }

}