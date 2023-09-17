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
        public static event EventHandler Connected;

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

        public async Task<IEnumerable<SftpFile>> ListDir(string dir=null)
        {
            dir ??= CurDir;

            
            var res = await Task.Run(() =>  { return sessionBackground.ListDirectory(dir); });
            return res;
        }

        // can use the mask to download specific files, leave null to download everything from folder (no hidden folders)
        public TransferInfo EnqueueFolderDownload(string source, string target)
        {
            if (session == null) throw new Exception("No session");

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

        public TransferInfo EnqueueFileDownload(string source, string target, bool thumbnail=false)
        {
            if (session == null) throw new Exception("No session");
            Debug.WriteLine("Enquing file download");
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
        // return the local dir of thumbnail folder for given directory path
        public static string GetThumbnailFolder(string dir)
        {
            string path = Path.Join(FileSystem.CacheDirectory, "DirectSFTP");
            Debug.WriteLine(path);
            return Path.Join(path,dir.Replace("/", "")+".dthumb");
        }
        // join path with fileName
        public static string RemoteJoinPath(string path, string name)
        {
            return path+ "/" + name;
        }

        public void DeleteFile(string path)
        {
            sessionBackground.Delete(path);
        }

        public void DeleteDirectory(string dir)
        {
            Tuple<List<SftpFile>, long> info = null;
            List<SftpFile> allFiles = new();

            try
            {
                info = GetFilesRecursive(dir, allFiles);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + "No Permissions to delete dir");
                return;
            }

            foreach(SftpFile file in allFiles)
            {
                if (file.IsDirectory == false)
                {
                    try
                    {
                        session.Delete(file.FullName);
                    }catch(Exception)
                    {
                        Debug.WriteLine("Can't delete " + file.FullName);
                    }
                }
            }

            foreach (SftpFile file in allFiles)
            {
                if (file.IsDirectory == false)
                {
                    try
                    {
                        session.Delete(file.FullName);
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine("Can't delete " + file.FullName);
                    }
                }
            }


        }
        
        private void DownloadFolder(TransferInfo transfer)
        {
            Debug.WriteLine("Starting");

            string sourceDirName = RemoteGetFileName(transfer.SourcePath);
            int prefixSize = RemoteGetDirName(transfer.SourcePath).Length;

            transfer.Status = "Calculating";
            TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.CalculatingDistances));
            List<SftpFile> allFiles = new();

            
            Tuple<List<SftpFile>,long> info = null;
            try
            {
                info = GetFilesRecursive(transfer.SourcePath, allFiles, transfer.Thumbnails);
            }catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + "WHat");
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                //TODO HANDLE
                return;
            }


            Stopwatch sw = Stopwatch.StartNew();
            double totalSize = info.Item2;
            double totalRead = 0;
            transfer.Size = totalSize/1000000.0;

            transfer.Status = "Downloading";
            try
            {
                foreach (var file in allFiles)
                {
                    
                    string suffix = RemoteGetDirName(file.FullName[(prefixSize + 1)..]);
                    string targetDir = Path.Join(transfer.TargetPath, suffix);

                    DownloadFile(transfer, totalSize, file.FullName, targetDir, totalRead, false);

                    totalRead += file.Length;
                    if (transfer.Cancel) { break; }
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
        private Tuple<List<SftpFile>,long> GetFilesRecursive(string path, List<SftpFile> list, bool thumbnail=false)
        {
            var dirFiles = session.ListDirectory(path);
            long totalSize = 0;

            if (thumbnail)
            {
                foreach(var file in dirFiles)
                {
                    if (file.Name.StartsWith('.') || file.IsDirectory)  continue;
                    list.Add(file);
                    totalSize += file.Length;
                }
                return new(list,totalSize);
            }
            
            foreach (var file in dirFiles)
            {
                if (file.Name.StartsWith('.')) continue;
                if (file.IsDirectory)
                {
                    try
                    {
                        var res = GetFilesRecursive(file.FullName, list, thumbnail);
                        totalSize += res.Item2;
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
            if (session.IsConnected == false) throw new Exception("Not connected");

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
            CleanupAfterTransfer();
        }

        
        private void CleanupAfterTransfer()
        {
            working = false;
            curTrans = null;
        }
    }
}
