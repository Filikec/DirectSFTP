using System.Diagnostics;
using System.Security.Cryptography;
using WinSCP;

namespace DirectSFTP
{
    public enum TransferEventType
    {
        Finished,
        Progress,
        Cancelled,
        Error
    }
    public class SFTP
    {
        private static readonly object lockObj = new();
        private static SFTP instance;
        private static int Id = 0;
        private Session session;
        private static bool working = false;
        public static int curId { get; private set; } = 0;
        private static bool cancel = false;
        public static string CurDir = "/";

        // handlers for different transfers
        public Dictionary<int, EventHandler<Tuple<int,TransferEventType>>> TransferEvents { get; private set; }
        public List<TransferInfo> Transfers { get; private set; }

        private SFTP() {
            session = new Session();
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

        public Session GetSession()
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
                    if (cancel)
                    {
                        cancel = false;
                        b.Cancel = true;
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

            if (mask == null && !thumbnails)
            {
                mask = "*|.*/;.*";
            }

            TransferInfo newTransfer = new(Id++)
            {
                   SourcePath = source,
                   TargetPath = target,
                   Thumbnails = thumbnails,
                   Mask = mask,
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
            string nameOnly = Path.GetFileName(source);
            return EnqueueFolderDownload(pathOnly, target, false, nameOnly);
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
            curId = curTransfer.Id;

            Debug.WriteLine("Started transferring " + curTransfer.SourcePath + " with id " + curTransfer.Id);

            if (curTransfer.Type == TransferType.Download)
            {
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
                        Debug.WriteLine("Finished " + curTransfer.Id);
                        TransferEvents[curTransfer.Id]?.Invoke(curTransfer, new(curTransfer.Id, TransferEventType.Finished));
                        TransferEvents.Remove(curTransfer.Id);
                    });
                
                await res;
            }
        }

        public void CancelTransfer(int id)
        {
            if (working && curId == id)
            {
                cancel = true;
            }
            else
            {
                Transfers.RemoveAll(t => t.Id == id);
                TransferEvents.Remove(id);
            }
        }

        // get the directory name (.../a/b -> .../a ; .../a/b.ext -> .../a)
        public string RemoteGetDirName(string path)
        {
            if (path == null || path == "/") return path;

            return path[..path.LastIndexOf('/')];
        }

        public string RemoteGetFileName(string path)
        {
            if (path == null || path == "/") return path;
            return path[(path.LastIndexOf('/')+1)..];
        }
    }
}
