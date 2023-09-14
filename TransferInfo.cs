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
        public float Progress { get; set; }
        
        public TransferInfo(int id)
        {
            Id = id;
            Progress = 0;
        }
        
    }
}
