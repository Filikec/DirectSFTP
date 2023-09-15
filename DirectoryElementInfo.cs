
using Renci.SshNet.Sftp;

namespace DirectSFTP
{
    public class DirectoryElementInfo : BindableObject
    {
        private bool imgUpdated = false;
        public SftpFile FileInfo { get; set; }
        public string ImagePath { get; set; }
        public Command OnClick { get; set; }
        public Command OnDownload { get; set; }
        public void UpdatedImg()
        {
            if (!imgUpdated)
            {
                imgUpdated = true;
                OnPropertyChanged(nameof(ImagePath));
            }
        }

    }
}
