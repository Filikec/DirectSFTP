using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
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
        private SftpClient session;
        private static bool working = false;
        public static TransferInfo curTrans { get; private set; } = null;
        public static bool IsConnected { get; private set; } = false;
        private static bool cancel = false;
        public static string CurDir = "/";
        public static event EventHandler Connected;

        // handlers for different transfers
        public Dictionary<int, EventHandler<Tuple<int,TransferEventType>>> TransferEvents { get; private set; }
        public List<TransferInfo> Transfers { get; private set; }


        private SFTP() {
            TransferEvents = new();
            Transfers = new();
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

        public async void Connect(string host, int port, string user, string pswd)
        {
             await Task.Run( () => {
                 session = new(host,port,user,pswd);
                 session.Connect();
                 Debug.WriteLine("Connected");
                 IsConnected = true;
                 Connected?.Invoke(this, EventArgs.Empty); 
             });
            
        }

        public async Task<IEnumerable<SftpFile>> ListDir(string dir=null)
        {
            dir ??= CurDir;

            var res = await Task.Run(() =>  { return session.ListDirectory(dir); });

            return res;
        }

        // can use the mask to download specific files, leave null to download everything from folder (no hidden folders)
        public TransferInfo EnqueueFolderDownload(string source, string target, bool thumbnails=false)
        {
            if (session == null) throw new Exception("No session");

            Debug.WriteLine("Enquing dir download");

            TransferInfo newTransfer = new(Id++)
            {
                   SourcePath = source,
                   TargetPath = target,
                   Thumbnails = thumbnails,
                   Title = source,
                   SingleFile = false,
                   Type = TransferType.Download
            };

            Transfers.Add(newTransfer);
            TransferEvents.Add(newTransfer.Id,null);

            ContinueWork();

            return newTransfer;
        }

        public TransferInfo EnqueueFileDownload(string source, string target)
        {
            if (session == null) throw new Exception("No session");
            Debug.WriteLine("Enquing file download");
            TransferInfo newTransfer = new(Id++)
            {
                SourcePath = source,
                TargetPath = target,
                Thumbnails = false,
                Title = source,
                SingleFile = true,
                Type = TransferType.Download
            };

            Transfers.Add(newTransfer);
            TransferEvents.Add(newTransfer.Id, null);

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
                    DownloadFile(curTrans);
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

        public void CancelTransfer(int id)
        {
            
            if (working && curTrans.Id == id)
            {
                curTrans.Cancel = true;
            }
            else
            {
                Transfers.RemoveAll(t => t.Id == id);
                TransferEvents.Remove(id);
            }
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
                info = GetFilesRecursive(transfer.SourcePath, allFiles, transfer.Thumbnails);
            }catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                //TODO HANDLE
                return;
            }


            Stopwatch sw = Stopwatch.StartNew();
            long totalSize = info.Item2;
            long totalRead = 0;
            transfer.Size = totalSize/1000000.0;

            try
            {
                foreach (SftpFile file in info.Item1)
                {
                    long prevRead = 0;
                    string suffix = RemoteGetDirName(file.FullName[(prefixSize+1)..]);
                    string curDir = Path.Join(transfer.TargetPath, suffix);
                    string localFilePath = Path.Join(curDir, file.Name);
                    
                    if (Directory.Exists(curDir)==false)  Directory.CreateDirectory(curDir);

                    using var localFile = File.Create(localFilePath);

                    session.DownloadFile(file.FullName, localFile, (bytesRead) => {

                        if (transfer.Cancel)
                        {
                            localFile.Close();
                        }
                        else
                        {
                            long dif = (long)bytesRead - prevRead;
                            prevRead = (long)bytesRead;
                            totalRead += dif;
                            
                            transfer.Status = "Downloading";
                            transfer.Progress = ((double)totalRead) / totalSize;
                            transfer.TransSpeed = totalRead / sw.ElapsedMilliseconds / 1000.0;
                            TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Progress));
                        }
                    });
                }
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch (Exception ex)
            {
                if (curTrans.Cancel)
                {
                    Debug.WriteLine("Cancelled");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Cancelled));
                }
                else
                {
                    Debug.WriteLine("Error");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                }
            }
            sw.Stop();
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
                        Debug.WriteLine($"{ex.Message}");
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

        private void DownloadFile(TransferInfo transfer)
        {

            if (session.IsConnected == false) throw new Exception("Not connected");

            string localFilePath = Path.Join(transfer.TargetPath,RemoteGetFileName(transfer.SourcePath));

            try
            {
                double totalSize = session.GetAttributes(transfer.SourcePath).Size;
                
                transfer.Size = totalSize/1000000.0;
                using var file = File.Create(localFilePath);
                Stopwatch sw = Stopwatch.StartNew();

                session.DownloadFile(transfer.SourcePath, file, (bytesRead) => {
                    if (transfer.Cancel)
                    {
                        file.Close();
                    }
                    else
                    {
                        transfer.Status = "Downloading";
                        transfer.Progress = bytesRead / totalSize;
                        transfer.TransSpeed = ((double)bytesRead) / sw.ElapsedMilliseconds / 1000.0;
                        TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Progress));
                    }
                });
                sw.Stop();
                TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Finished));
            }
            catch (Exception ex)
            {
                if (curTrans.Cancel)
                {
                    Debug.WriteLine("Cancelled");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Cancelled));
                }
                else
                {
                    Debug.WriteLine("Error");
                    TransferEvents[transfer.Id]?.Invoke(transfer, new(transfer.Id, TransferEventType.Error));
                }
            }
        }
        private void CleanupAfterTransfer()
        {
            working = false;
            curTrans = null;
        }
    }
}
