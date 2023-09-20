using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace DirectSFTP
{
    public enum TransferEventType
    {
        Finished,
        Progress,
        Cancelled,
        Error,
        Created,
        CalculatingDistances
    }
    public class SFTP
    {
        private static readonly object lockObj = new();
        private static SFTP instance;
        private static int Id = 0;
        private SftpClient session, sessionBackground;
        private static bool working = false;
        public static TransferInfo curTrans { get; private set; } = null;
        public static bool IsConnected { get; private set; } = false;
        public static string CurDir = "/";
        public static event EventHandler Connected, UpdateCurrentDir;

        // handlers for different transfers
        public Dictionary<int, EventHandler<Tuple<int,TransferEventType>>> TransferEvents { get; private set; }
        public List<TransferInfo> Transfers { get; private set; }

        private SFTP() {
            TransferEvents = new();
            Transfers = new();
        }
        public static string GetDownloadFolder()
        {
            return Preferences.Default.Get("DownloadFolder", "");
        }
        public static SFTP GetInstance()
        {
            if (instance == null)
            {
                instance = new SFTP();
            }
            
            return instance;
        }

        public SftpClient GetSession()
        {
            lock (lockObj) { return  session; }
        }
        public SftpClient GetSessionList()
        {
            lock (lockObj) { return sessionBackground; }
        }

        public bool Connect(string host, int port, string user, string pswd)
        {
            session = new(host,port,user,pswd);
            sessionBackground = new(host, port, user, pswd);
            try
            {
                session.Connect();
                sessionBackground.Connect();
                IsConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);
            }catch(Exception)
            {
                return false;
            }
                 
            return true;
        }

        // Update the string
        // null == current dir
        public async Task<IEnumerable<SftpFile>> ListDir(string dir=null)
        {
            dir ??= CurDir;
            
            var res = await Task.Run(() =>  { return sessionBackground.ListDirectory(dir); });
            return res;
        }

        public void EnqueueDirUpdate(string dir)
        {
            TransferInfo newTransfer = new(Id++)
            {
                SourcePath = dir,
                TargetPath = "",
                Thumbnails = false,
                Title = "dir list",
                SingleFile = false,
                Type = TransferType.ListDir
            };

            Transfers.Add(newTransfer);

            ContinueWork();
        }

        // can use the mask to download specific files, leave null to download everything from folder (no hidden folders)
        public TransferInfo EnqueueFolderDownload(string source, string target)
        {
            Debug.WriteLine("Enquing dir download");

            TransferInfo newTransfer = new(Id++)
            {
                   SourcePath = source,
                   TargetPath = target,
                   Thumbnails = false,
                   Title = source,
                   SingleFile = false,
                   Type = TransferType.Download
            };

            TransferEvents.Add(newTransfer.Id, null);
            Transfers.Add(newTransfer);

            ContinueWork();

            return newTransfer;
        }

        public TransferInfo EnqueueFolderUpload(string source, string target)
        {
            Debug.WriteLine("Enquing dir upload");

            TransferInfo newTransfer = new(Id++)
            {
                SourcePath = source,
                TargetPath = target,
                Thumbnails = false,
                Title = source,
                SingleFile = false,
                Type = TransferType.Upload
            };

            TransferEvents.Add(newTransfer.Id, null);
            Transfers.Add(newTransfer);

            ContinueWork();

            return newTransfer;
        }

        public TransferInfo EnqueueFileUpload(string source, string target)
        {
            if (session == null) throw new Exception("No session");

            Debug.WriteLine("Enquing file upload");

            TransferInfo newTransfer = new(Id++)
            {
                SourcePath = source,
                TargetPath = target,
                Thumbnails = false,
                Title = source,
                SingleFile = true,
                Type = TransferType.Upload
            };

            TransferEvents.Add(newTransfer.Id, null);
            Transfers.Add(newTransfer);

            ContinueWork();

            return newTransfer;
        }

        public TransferInfo EnqueueFileDownload(string source, string target, bool thumbnail=false)
        {
            TransferInfo newTransfer = new(Id++)
            {
                SourcePath = source,
                TargetPath = target,
                Thumbnails = thumbnail,
                Title = source,
                SingleFile = true,
                Type = TransferType.Download
            };

            TransferEvents.Add(newTransfer.Id, null);

            if (thumbnail)
            {
                Task.Run(()=> {
                    double size = sessionBackground.GetAttributes(source).Size;
                    DownloadFile(newTransfer,size,source,target);
                });

                return newTransfer;
            }
            Transfers.Add(newTransfer);
      
            ContinueWork();

            return newTransfer;
        }
        public TransferInfo EnqueueDelete(IReadOnlyList<DirectoryElementInfo> items, string curDir)
        {
            TransferInfo newTransfer = new(Id++)
            {
                Title = "Deleting Files",
                SingleFile = items.Count == 1,
                Type = TransferType.Delete,
                FilesToDelete = items,
                SourcePath = curDir,
            };

            Debug.WriteLine("Enqeuing delete");

            TransferEvents.Add(newTransfer.Id, null);
            Transfers.Add(newTransfer);

            ContinueWork();

            return newTransfer;
        }

        private async void ContinueWork()
        {
            if (working) return;
            if (session == null) throw new Exception("No session");
            if (Transfers.Count == 0)
            {
                working = false;
                return;
            }

            var curTransfer = Transfers.First();
            Transfers.RemoveAt(0);

            working = true;
            curTrans = curTransfer;

            Debug.WriteLine("Started transferring " + curTransfer.SourcePath + " with id " + curTransfer.Id + " into " + curTransfer.TargetPath);

            if (curTransfer.Type == TransferType.Download)
            {
                if (curTrans.SingleFile) await Task.Run(() => {
                    double size = session.GetAttributes(curTrans.SourcePath).Size;
                    curTrans.Status = "Downloading";
                    curTrans.Size = size/1000000.0;
                    DownloadFile(curTrans,size,curTrans.SourcePath,curTrans.TargetPath);
                    CleanupAfterTransfer();
                    ContinueWork();
                });
                else await Task.Run(() => {
                    DownloadFolder(curTrans);
                    CleanupAfterTransfer();
                    ContinueWork();
                });
            }
            else if (curTransfer.Type == TransferType.Upload)
            {
                if (curTrans.SingleFile)
                {
                    await Task.Run(() => {
                        var size = new FileInfo(curTrans.SourcePath).Length;
                        curTrans.Status = "Uploading";
                        curTrans.Size = size / 1000000.0;
                        UploadFile(curTrans, curTrans.SourcePath, curTrans.TargetPath, size);
                        CleanupAfterTransfer();
                        ContinueWork();
                    });
                }
                else
                {
                    await Task.Run(() => {
                        UploadFolder(curTransfer);
                        CleanupAfterTransfer();
                        ContinueWork();
                    });
                }
            }else if (curTransfer.Type == TransferType.Delete)
            {
                await Task.Run(() => {
                    Delete(curTransfer);
                    CleanupAfterTransfer();
                    ContinueWork();
                });
            }else if (curTransfer.Type == TransferType.ListDir)
            {
                await Task.Run(() => {
                    if (curTransfer.SourcePath == CurDir)
                    {
                        UpdateCurrentDir?.Invoke(this, EventArgs.Empty);
                    }
                    CleanupAfterTransfer();
                    ContinueWork();
                });
            }
        }

        public void CancelTransfer(TransferInfo transfer)
        {
            transfer.Cancel = true;
            
            Transfers.RemoveAll(t => t.Id == transfer.Id);
            if (curTrans != null && curTrans.Id != transfer.Id) TransferEvents.Remove(transfer.Id);
        }

        // get the directory name for the server path (.../a/b -> .../a ; .../a/b.ext -> .../a)
        public static string RemoteGetDirName(string path)
        {
            if (path == null || path == "/") return path;

            path = path[..path.LastIndexOf('/')];

            if (path == "") return "/";
            else return path;
        }

        // get the file name from server path
        public static string RemoteGetFileName(string path)
        {
            if (path == null || path == "/") return path;
            path = path[(path.LastIndexOf('/')+1)..];

            return path;
        }
        
        // join path with fileName
        public static string RemoteJoinPath(string path, string name)
        {
            return path+ "/" + name;
        }
        // return the local dir of thumbnail folder for given directory path
        public static string GetThumbnailFolder(string dir)
        {
            string path = Path.Join(FileSystem.CacheDirectory, "DirectSFTP");
            Debug.WriteLine(path);
            return Path.Join(path, dir.Replace("/", "") + ".dthumb");
        }
        
        private void UploadFolder(TransferInfo transfer)
        {
            string sourceDirName = Path.GetFileName(transfer.SourcePath);
            int prefixSize = Path.GetDirectoryName(transfer.SourcePath).Length;

            Debug.WriteLine("Uploading dir " + sourceDirName);

            transfer.Status = "Calculating";
            TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.CalculatingDistances));

            Debug.WriteLine("Calculating distances");

            string[] files = Directory.GetFiles(transfer.SourcePath, "*.*", SearchOption.AllDirectories);

            double totalSize = 0;
            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    totalSize += new FileInfo(file).Length;
                }
            }
            transfer.Size = totalSize / 1000000.0;
            transfer.Status = "Downloading";
            double totalWrote = 0;

            Debug.WriteLine("Uploading files");
            try
            {
                foreach (string file in files)
                {
                    if (File.Exists(file))
                    {
                        string suffix = Path.GetDirectoryName(file[(prefixSize + 1)..]);
                        suffix = suffix.Replace('\\', '/');
                        string targetDir = RemoteJoinPath(transfer.TargetPath, suffix);
                        
                        if (session.Exists(targetDir)==false)
                        {
                            CreateDirRec(targetDir);
                        }

                        UploadFile(transfer, file, targetDir, totalSize, totalWrote, false);
                        totalWrote += new FileInfo(file).Length;
                    }
                    if (transfer.Cancel) break;
                }
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch (Exception)
            {
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
            }
        }

        private void DownloadFolder(TransferInfo transfer)
        {
            string sourceDirName = RemoteGetFileName(transfer.SourcePath);
            int prefixSize = RemoteGetDirName(transfer.SourcePath).Length;

            transfer.Status = "Calculating";
            TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.CalculatingDistances));
            List<SftpFile> allFiles = new();

            
            Tuple<List<SftpFile>,long> info = null;
            try
            {
                info = GetFilesRecursive(transfer.SourcePath, allFiles);
            }catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + "WHat");
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                //TODO HANDLE
                return;
            }


            double totalSize = info.Item2;
            double totalRead = 0;
            transfer.Size = totalSize/1000000.0;

            Debug.WriteLine("Downlaoding folder " + sourceDirName);

            transfer.Status = "Downloading";
            try
            {
                foreach (var file in allFiles)
                {
                    
                    if (file.IsDirectory==false)
                    {
                        string suffix = RemoteGetDirName(file.FullName[(prefixSize + 1)..]);
                        string targetDir = Path.Join(transfer.TargetPath, suffix);

                        DownloadFile(transfer, totalSize, file.FullName, targetDir, totalRead, false);

                        totalRead += file.Length;
                        if (transfer.Cancel) { break; }
                    }

                    
                }
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch(Exception e)
            {
                Debug.WriteLine(e);
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
            }
            
        }

        // gets all files in folder and subfolders (disregards hidden folders) and their total size
        private Tuple<List<SftpFile>,long> GetFilesRecursive(string path, List<SftpFile> list, bool includeHidden=false)
        {
            var dirFiles = session.ListDirectory(path);
            long totalSize = 0;
            
            foreach (var file in dirFiles)
            {
                if (file.Name == "." || file.Name == "..") continue;
                if (file.Name.StartsWith('.') && includeHidden==false) continue;

                if (file.IsDirectory)
                {
                    try
                    {
                        var res = GetFilesRecursive(file.FullName, list, includeHidden);
                        totalSize += res.Item2;
                        list.Add(file);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{ex.Message} + <");
                    }
                }
                else
                {
                    list.Add(file);
                    totalSize += file.Length;
                }
            }
            
            return new(list,totalSize);

        }

        private void DownloadFile(TransferInfo transfer, double totalSize, string fileRemotePath, string folderTargetPath, double totalRead = 0, bool signalDone=true)
        {

            string localFilePath = Path.Join(folderTargetPath, RemoteGetFileName(fileRemotePath));
            if (Directory.Exists(folderTargetPath) == false) Directory.CreateDirectory(folderTargetPath);

            Debug.WriteLine("Downloading " + fileRemotePath + " into " + localFilePath);
            try
            {
                var sessionUsed = session;
                if (transfer.Thumbnails) sessionUsed = sessionBackground;

                using var file = File.Create(localFilePath);
                Stopwatch sw = Stopwatch.StartNew();
                double prevRead = 0;

                sessionUsed.DownloadFile(fileRemotePath, file, (bytesRead) => {
                    if (transfer.Cancel)
                    {
                        file.Close();
                    }
                    else
                    {
                        double dif = bytesRead - prevRead;
                        prevRead = bytesRead;
                        totalRead += dif;

                        transfer.Progress = totalRead * 100.0 / totalSize;
                        transfer.TransSpeed = ((double)bytesRead)  / sw.ElapsedMilliseconds / 1000.0;
                        TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Progress));
                    }
                });
                sw.Stop();
                if (signalDone) TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch (Exception e)
            {
                if (transfer.Cancel)
                {
                    Debug.WriteLine("Cancelled");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Cancelled));
                    File.Delete(localFilePath);
                }
                else
                {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine("Error");
                    if (signalDone) TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                    File.Delete(localFilePath);
                    throw new Exception("Error occured");
                }
            }
        }

        private void UploadFile(TransferInfo transfer,string sourcePath,string targetFolder, double totalSize, double totalWrote=0, bool signalDone=true)
        {
            string targetPath = RemoteJoinPath(targetFolder, Path.GetFileName(sourcePath));

            if (File.Exists(sourcePath) == false) throw new Exception("File Not present");

            Debug.WriteLine("Uploading " + sourcePath + "into " + targetPath);

            using var sourceFile = File.OpenRead(sourcePath);
            var sw = Stopwatch.StartNew();
            double prevWrote = 0;
            try
            {
                if (ImageHelper.IsImage(Path.GetFileName(sourcePath))){
                    Task.Run(() =>
                    {
                        UploadThumbnailImg(sourcePath, targetFolder);
                    });
                }
                
                session.UploadFile(sourceFile, targetPath, (bytesWrote) =>
                {
                    if (transfer.Cancel)
                    {
                        sourceFile.Close();
                    }
                    else
                    {
                        double dif = bytesWrote - prevWrote;
                        prevWrote = bytesWrote;
                        totalWrote += dif;
                        transfer.Progress = totalWrote * 100.0 / totalSize;
                        transfer.TransSpeed = ((double)bytesWrote) / sw.ElapsedMilliseconds / 1000.0;
                        TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Progress));
                    }
                });
                
                if (signalDone) TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch(Exception e)
            {
                if (transfer.Cancel)
                {
                    Debug.WriteLine("Cancelled");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Cancelled));
                    DeleteFile(targetPath);
                }
                else
                {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine("Error");
                    if (signalDone) TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                    File.Delete(targetPath);
                    throw new Exception("Error occured");
                }
            }
            sw.Stop();
        }

        private void UploadThumbnailImg(string originalImgPath, string targetFolder)
        {
            string pathToThumbnail = ImageHelper.CreateThumbnailFile(originalImgPath);
            using (var thumbnailFile = File.OpenRead(pathToThumbnail))
            {
                var targetThumbnailFolder = RemoteJoinPath(targetFolder, ".dthumb");
                Debug.WriteLine("Uploading thumbnail to " + targetThumbnailFolder);
                if (sessionBackground.Exists(targetThumbnailFolder) == false)
                {
                    sessionBackground.CreateDirectory(targetThumbnailFolder);
                }
                var targetThumbnailFile = RemoteJoinPath(targetThumbnailFolder, Path.GetFileName(originalImgPath));
                sessionBackground.UploadFile(thumbnailFile, targetThumbnailFile);
            }
            File.Delete(pathToThumbnail);
        }


        private void Delete(TransferInfo transfer)
        {
            double totalSize = transfer.FilesToDelete.Count;
            double deleted = 0;

            transfer.Status = "Deleting";
            try
            {
                foreach (var item in transfer.FilesToDelete)
                {
                    if (transfer.Cancel) break;
                    if (item.IsFile)
                    {
                        DeleteFile(item.FileInfo.FullName);
                    }
                    else
                    {
                        DeleteDirectory(item.FileInfo.FullName);
                    }
                    deleted++;
                    transfer.Progress = deleted * 100.0 / totalSize;
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Progress));
                }
                if (transfer.Cancel)
                {
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Cancelled));
                }
                else
                {
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
                }
            }catch (Exception)
            {
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
            }
        }

        private void DeleteFile(string path)
        {
            session.Delete(path);
        }

        private void DeleteDirectory(string dir)
        {
            List<SftpFile> allFiles = new();
            try
            {
                Tuple<List<SftpFile>, long> info = GetFilesRecursive(dir, allFiles, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + "No Permissions to delete dir");
                return;
            }
            Debug.WriteLine("Deleting items");
            // first delete all files
            foreach (SftpFile file in allFiles)
            {
                if (file.IsDirectory == false)
                {
                    try
                    {
                        Debug.WriteLine("Deleting " + file.FullName);
                        session.Delete(file.FullName);
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine("Can't delete " + file.FullName);
                    }
                }
            }

            Debug.WriteLine("sorting");
            allFiles.Sort((a, b) => b.Length.CompareTo(a.Length));
            Debug.WriteLine("Deleting folders");
            foreach (SftpFile file in allFiles)
            {
                if (file.IsDirectory)
                {
                    try
                    {
                        Debug.WriteLine("Deleting dir " + file);
                        session.DeleteDirectory(file.FullName);
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine("Can't delete " + file.FullName);
                    }
                }
            }

            Debug.WriteLine("Deleting root dir");
            try
            {
                Debug.Write(session.Exists(dir));
                Debug.Write("deleting dir " + dir);
                session.DeleteDirectory(dir);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e + " WAS");
            }


        }

        private void CleanupAfterTransfer()
        {
            working = false;
            if (TransferEvents.ContainsKey(curTrans.Id))
            {
                TransferEvents.Remove(curTrans.Id);
            }
            curTrans = null;
            
        }

        // Takes a path and for each level checks if it exists
        // If not, it creates it so that the entire path is creates
        // (/a/b) would not otherwise be created if (/a) doesn't exist
        private void CreateDirRec(string path)
        {
            var levels = path.Split('/');

            var curPath = "";

            foreach (var level in levels)
            {
                if (level != "")
                {
                    curPath = RemoteJoinPath(curPath, level);
                    if (session.Exists(curPath) == false)
                    {
                        session.CreateDirectory(curPath);
                    }
                }
            }       
        }
    }
}
