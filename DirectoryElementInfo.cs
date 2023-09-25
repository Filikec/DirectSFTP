
using Renci.SshNet.Sftp;

namespace DirectSFTP
{
    public class DirectoryElementInfo : BindableObject
    {
        public bool ImgUpdated { get; private set; } = false;
        public bool TriedDownload { get;  set; } = false;
        public SftpFile FileInfo { get; set; }
        public string ImagePath { get; set; }
        public Command OnClick { get; set; }

        public double Size { get { return FileInfo.Length / 1000000.0; }  }
        public bool IsFile { get { return !FileInfo.IsDirectory; } }

        private bool selected = false;

        public bool Selected { get { return selected; } set { selected = value; OnPropertyChanged(nameof(Selected)); } }
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
