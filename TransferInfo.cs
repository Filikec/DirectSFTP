using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinSCP;

namespace DirectSFTP
{

    public enum TransferType
    {
        Download,
        Upload
    }
    public class TransferInfo : BindableObject
    {
        public TransferType Type { get; set; }
        public string Mask { get; set; }
        public bool Thumbnails { get; set; } // whether the transfer is supposed to download thumbnails (from .dthumb folder)
        public string TargetPath { get; set; }
        public string SourcePath { get; set; }
        public int Id { get; private set; }
        public double Progress { get; set; }
        public double TransSpeed { get; set; }
        public string Status { get; set; }
        public Command OnCancel { get; set; }
        public Command OnDownload { get; set; }
        public bool SingleFile { get; set; }
        public string Title { get; set; }
        public TransferInfo(int id)
        {
            Id = id;
            Progress = 0;
            Status = "Queued";
            SingleFile = false;
        }

        public void UpdateProgress()
        {
            OnPropertyChanged(nameof(TransSpeed));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(Status));
        }
       
        
    }
}
