
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
