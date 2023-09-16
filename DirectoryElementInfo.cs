
using Renci.SshNet.Sftp;

namespace DirectSFTP
{
    public class DirectoryElementInfo : BindableObject
    {
        public bool ImgUpdated { get; private set; } = false;
        public bool Updating { get;  set; } = false;
        public SftpFile FileInfo { get; set; }
        public string ImagePath { get; set; }
        public Command OnClick { get; set; }
        public Command OnDownload { get; set; }
        public void UpdatedImg()
        {
            if (!ImgUpdated)
            {
                ImgUpdated = true;
                OnPropertyChanged(nameof(ImagePath));
            }
        }

    }
}
