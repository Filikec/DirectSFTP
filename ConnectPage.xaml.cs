using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SharpHook;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;

namespace DirectSFTP;

public partial class ConnectPage : ContentPage
{
    private ObservableCollection<DirectoryElementInfo> dirElements = new();
    private ObservableCollection<TransferInfo> transfers = new();
    private SFTP sftp;
    private SelectedOptions selectedOptions;
    private Stack<string> prevPaths;
    private bool mouseInWindow = false;
    private static bool setup = false;

    public ConnectPage()
    {
        InitializeComponent();

        prevPaths = new();
        sftp = SFTP.GetInstance();

        
        AddHooks();
        
        

        
        SetupDirView();

        Loaded += (a, b) =>
        {
            if (SFTP.IsConnected) Task.Run(() => { UpdateDir(SFTP.CurDir); });
            else SFTP.Connected += (a, b) => Task.Run(() => { UpdateDir(SFTP.CurDir); });
        };

        SFTP.UpdateCurrentDir += (a, b) =>
        {
            Debug.WriteLine("Event to update dir raised");
            UpdateDir(SFTP.CurDir);
        };

        Command onClear = new Command(() => Dispatcher.Dispatch(()=>dirView.SelectedItems.Clear()));
        selectedOptions = new(this,selectionStack,onClear);


#if WINDOWS
        AddDropOptionsWindows();
#endif

    }

    private void OnParentFolderClick(object sender, EventArgs e)
    {
        string path = SFTP.CurDir;

        Debug.WriteLine(path + " Parent requested");
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
        sftp.EnqueueDirUpdate(SFTP.CurDir);

        Debug.WriteLine(e.ToString());
    }
    private void OnReload(object sender, EventArgs e)
    {
        UpdateDir(SFTP.CurDir);
    }

    private async void OnCreateFolder(object sender, EventArgs e)
    {
        string result = await DisplayPromptAsync("Input", "Enter your text:");
        if (result == "")
        {
            await DisplayAlert("Result", "Can't created such folder", "OK");
        }
        else
        {
            string newFolderPath = SFTP.RemoteJoinPath(SFTP.CurDir,result);
            if (sftp.GetSessionList().Exists(newFolderPath))
            {
                await DisplayAlert("Result", "Folder already exists", "OK");
            }
            else
            {
                sftp.GetSessionList().CreateDirectory(newFolderPath);
                await DisplayAlert("Result", "Folder created", "OK");
                UpdateDir(SFTP.CurDir);
            }
        }

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
        await Dispatcher.DispatchAsync(() => { 
            Title = dir;
        });

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
            
            if (ImageHelper.IsImage(file.Name))
            {
                string thumbnail = Path.Join(thumbFolder, file.Name);
                if (File.Exists(thumbnail))
                {
                    dirElements.Last().ImagePath = thumbnail;
                }
                if (i < 6 && dirElements.Last().TriedDownload == false) DownloadThumbnail(dirElements.Last());
            }
            else if (file.IsDirectory==false)
            {
                dirElements.Last().ImagePath = "documents.png";
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

    public void EnqueueFolderDownload(string folderPath, string target)
    {
        TransferInfo transfer = sftp.EnqueueFolderDownload(folderPath, target);
        CreateTransferButton(transfer);
    }

    public void EnqueueFolderUpload(string folderPath, string target)
    {
        TransferInfo transfer = sftp.EnqueueFolderUpload(folderPath, target);
        CreateTransferButton(transfer);
    }

    public void EnqueueDelete(IReadOnlyList<DirectoryElementInfo> items)
    {
        TransferInfo transfer = sftp.EnqueueDelete(items,SFTP.CurDir);
        sftp.EnqueueDirUpdate(SFTP.CurDir);
        CreateTransferButton(transfer);
    }


    private async void DownloadThumbnail(DirectoryElementInfo file)
    {
        file.TriedDownload = true;

        string remotePath = SFTP.RemoteJoinPath(SFTP.RemoteGetDirName(file.FileInfo.FullName),".dthumb");
        remotePath = SFTP.RemoteJoinPath(remotePath, file.FileInfo.Name);

        string thumbFolder = SFTP.GetThumbnailFolder(SFTP.RemoteGetDirName(file.FileInfo.FullName));

        string thumbnailPath = Path.Join(thumbFolder, file.FileInfo.Name);

        if (File.Exists(thumbnailPath)) {
            var fileUpdTime = new FileInfo(thumbnailPath).LastWriteTime;
            
            if (fileUpdTime >= file.FileInfo.LastWriteTime)
            {
                Debug.WriteLine("Don't need to download thumbnail");
                return;
            }

        }

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
                file.ImagePath = thumbnailPath;
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

        if (e.CurrentSelection.Count == 1)
        {
            var file = (DirectoryElementInfo)e.CurrentSelection[0];
            selectedOptions.ShowRename(true);
            SetSelectionOnRename(file);
        }
        else
        {
            selectedOptions.ShowRename(false);
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
            var result = DisplayAlert("Confirmation", "Do you want to delete?", "Yes", "No");
            result.ContinueWith(t =>
            {
                if (t.Result == false) return;
                List<DirectoryElementInfo> dirItems = new();
                foreach (DirectoryElementInfo item in items)
                {
                    dirItems.Add(item);
                }
                EnqueueDelete(dirItems);
            });
        }));
    }

    private void SetSelectionOnRename(DirectoryElementInfo curFile)
    {
        selectedOptions.SetOnRename(new Command(async () =>
        {
            string result = await DisplayPromptAsync("Input", "New name (don't forget extension):");
            if (result == "")
            {
                await DisplayAlert("Result", "Can't create document without name", "OK");
                return;
            }
            string newName = SFTP.RemoteJoinPath(SFTP.CurDir, result);

            if (sftp.GetSessionList().Exists(newName))
            {
                await DisplayAlert("Result", "Name already exists", "OK");
                return;
            }

            sftp.GetSessionList().RenameFile(curFile.FileInfo.FullName, newName);
            if (ImageHelper.IsImage(curFile.FileInfo.Name))
            {
                string thumbnailFolder = SFTP.RemoteJoinPath(SFTP.CurDir, ".dthumb");
                string thumbnailPath = SFTP.RemoteJoinPath(thumbnailFolder, curFile.FileInfo.Name);
                if (sftp.GetSessionList().Exists(thumbnailPath))
                {
                    string newThumbail = SFTP.RemoteJoinPath(thumbnailFolder, result);
                    sftp.GetSessionList().RenameFile(thumbnailPath, newThumbail);
                }
            }
            await DisplayAlert("Result", "File renamed to " + result, "OK");
            Dispatcher.Dispatch(()=>UpdateDir(SFTP.CurDir));

        }));
    }

    private void AddHooks()
    {        
        GlobalHooks.hooks.KeyPressed += (a, b) =>
        {
            if (!mouseInWindow) return;

            if (b.Data.KeyCode == SharpHook.Native.KeyCode.VcEscape && dirView.SelectedItems.Count > 0)
            {
                Dispatcher.Dispatch(() => dirView.SelectedItems.Clear());
            }
            else if (b.Data.KeyCode == SharpHook.Native.KeyCode.VcF5)
            {
                UpdateDir(SFTP.CurDir);
            }
        };

        GlobalHooks.hooks.MousePressed += (a, b) =>
        {
            if (!mouseInWindow) return;

            if (b.Data.Button == SharpHook.Native.MouseButton.Button4)
            {
                OnParentFolderClick(null, null);
            }
            else if (b.Data.Button == SharpHook.Native.MouseButton.Button5 && prevPaths.Count > 0)
            {
                UpdateDir(prevPaths.First());
                prevPaths.Pop();
            }
        };
        
        
        
    }

    private void OnPointerEntered(object sender, PointerEventArgs e)
    {
        mouseInWindow = true;
    }

    private void OnPointerExited(object sender, PointerEventArgs e)
    {
        mouseInWindow = false;
    }
    private void SetupDirView()
    {
        dirView.ItemsSource = dirElements;
        transView.ItemsSource = transfers;
        dirView.Scrolled += (a, b) =>
        {
            for (int i = b.FirstVisibleItemIndex; i <= b.LastVisibleItemIndex; i++)
            {
                if (i < dirElements.Count && i >= 0 && dirElements[i].ImgUpdated == false)
                {
                    var fileInfo = dirElements[i];

                    if (ImageHelper.IsImage(fileInfo.FileInfo.Name) 
                        && fileInfo.TriedDownload == false) DownloadThumbnail(fileInfo);
                }
            }
        };
    }
#if WINDOWS
    private void AddDropOptionsWindows()
    {

        Loaded += (a, b) =>
        {
            Action<Microsoft.UI.Xaml.DragEventArgs> onDrop = async (b) =>
            {
                var items = await b.DataView.GetStorageItemsAsync();
                foreach (var item in items){
                    if (Directory.Exists(item.Path)){
                        EnqueueFolderUpload(item.Path,SFTP.CurDir);
                    }else if (File.Exists(item.Path)){
                        EnqueueFileUpload(item.Path,SFTP.CurDir);
                    }
                }
                sftp.EnqueueDirUpdate(SFTP.CurDir);
            };
            IElement element = dirView;
            var context = Handler?.MauiContext;
            var view = element.ToPlatform(context);
            ArgumentNullException.ThrowIfNull(context);
            DirectSFTP.Platforms.Windows.DropHelper.RegisterDrop(view,onDrop);

        };

    }
#endif
}