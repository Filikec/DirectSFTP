using System.Diagnostics;
using System.Security.Cryptography;
using Renci.SshNet;
using WinSCP;

namespace DirectSFTP
{
    public enum TransferEventType
    {
        Finished,
        Progress,
        Cancelled,
        Error,
        Created
    }
    public class SFTP
    {
        private static readonly object lockObj = new();
        private static SFTP instance;
        private static int Id = 0;
        private WinSCP.Session session;
        private static bool working = false;
        public static TransferInfo curTrans { get; private set; } = null;
        private static bool cancel = false;
        public static string CurDir = "/";

        // handlers for different transfers
        public Dictionary<int, EventHandler<Tuple<int,TransferEventType>>> TransferEvents { get; private set; }
        public List<TransferInfo> Transfers { get; private set; }

        private SFTP() {
            session = new WinSCP.Session();
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

        public WinSCP.Session GetSession()
        {
            lock (lockObj) { return  session; }
        }

        public async void Connect(string host, int port, string user, string pswd)
        {
           
            await Task.Run(() => {
                SessionOptions sessionOptions = new()
                {
                    Protocol = Protocol.Sftp,
                    HostName = host, 
                    UserName = user, 
                    Password = pswd, 
                    PortNumber = port, 
                    SshHostKeyPolicy = SshHostKeyPolicy.GiveUpSecurityAndAcceptAny,
                };

                session.FileTransferProgress += (a, b) =>
                {
                    curTrans.Progress = b.OverallProgress;
                    curTrans.TransSpeed = b.CPS/1000000.0; // to MB
                    if (curTrans.Status == "Queued")
                    {
                        if (curTrans.Type==TransferType.Download) curTrans.Status = "Downloading";
                        else curTrans.Status = "Uploading";
                    }

                    if (cancel)
                    {
                        cancel = false;
                        TransferEvents[curTrans.Id]?.Invoke(curTrans, new(curTrans.Id, TransferEventType.Cancelled));
                        b.Cancel = true;
                    }
                    else
                    {
                        TransferEvents[curTrans.Id]?.Invoke(curTrans, new(curTrans.Id, TransferEventType.Progress));
                    }
                };

                session.Open(sessionOptions);

                Debug.WriteLine("Session Connected");
            });
        }

        public async Task<RemoteDirectoryInfo> ListDir(string dir=null)
        {
            dir ??= CurDir;

            var res = await Task.Run(() =>  { return session.ListDirectory(dir); });

            return res;
        }

        // can use the mask to download specific files, leave null to download everything from folder (no hidden folders)
        public TransferInfo EnqueueFolderDownload(string source, string target, bool thumbnails=false, string mask=null)
        {
            if (session == null) throw new Exception("No session");

            if (!thumbnails)
            {
                if (mask == null)
                {
                    mask = "* | .* ; .*/";
                }
                else
                {
                    mask += " |  .* ; .*/";
                }
            }

            TransferInfo newTransfer = new(Id++)
            {
                   SourcePath = source,
                   TargetPath = target,
                   Thumbnails = thumbnails,
                   Mask = mask,
                   Title = source,
            };

            Transfers.Add(newTransfer);
            TransferEvents.Add(newTransfer.Id,null);

            ContinueWork();

            Debug.WriteLine("Created download for " + source + " with id " + newTransfer.Id);

            return newTransfer;
        }

        public TransferInfo EnqueueFileDownload(string source, string target)
        {
            string pathOnly = RemoteGetDirName(source);
            string nameOnly = RemoteGetFileName(source);
            Debug.WriteLine("Created download fo file " + nameOnly + " in " + pathOnly);
            var res = EnqueueFolderDownload(pathOnly, target, false, nameOnly);
            res.SingleFile = true;
            res.Title = source;
            return res;
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
                if (!Directory.Exists(curTransfer.TargetPath)) Directory.CreateDirectory(curTransfer.TargetPath);
                
                TransferOptions options = new() {
                    FileMask = curTransfer.Mask
                };
                var res = Task.Run(() => session.GetFilesToDirectory(
                        curTransfer.SourcePath,
                        curTransfer.TargetPath,
                        null,
                        false,
                        options
                    )).ContinueWith(t =>
                    {
                        TransferEvents[curTransfer.Id]?.Invoke(curTransfer, new(curTransfer.Id, TransferEventType.Finished));
                        TransferEvents.Remove(curTransfer.Id);
                        working = false;
                        curTrans = null;
                        ContinueWork();
                    });
                
                await res;
            }
        }

        public void CancelTransfer(int id)
        {
            if (working && curTrans.Id == id)
            {
                cancel = true;
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
            string path = Path.Join(Path.GetTempPath(), "DirectSFTP");
            return Path.Join(path,dir.Replace("/", "")+".dthumb");
        }
        // join path with fileName
        public static string RemoteJoinPath(string path, string name)
        {
            return path+ "/" + name;
        }
    }
}
