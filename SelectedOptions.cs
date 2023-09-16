using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

// Intended to be potentially platform dependent (toolbaritem on Android, menuitem on Windows)
namespace DirectSFTP
{
    internal class SelectedOptions
    {
        private ContentPage page;
        public MenuBarItem barItem;
        public MenuFlyoutItem download,rename,delete;
        public bool IsShowing { get; private set; }

        public SelectedOptions(ContentPage page)
        {
            this.page = page;
            delete = new()
            {
                Text = "Delete",
            };
            download = new()
            {
                Text = "Download",
            };
            rename = new()
            {
                Text = "Rename",
            };

            IsShowing = false;
            barItem = new()
            {
                download,
            };
            barItem.Text = "Action";

        }

        public void ShowItems()
        {
            page.MenuBarItems.Add(barItem);
            IsShowing = true;
        }

        public void HideItems()
        {
            page.MenuBarItems.Clear();
            IsShowing = false;
        }

        public void SetOnDownload(Command cmd)
        {
            download.Command = cmd;
        }
        public void SetOnRename(Command cmd)
        {
            rename.Command = cmd;
        }
        public void SetOnDelete(Command cmd)
        {
            delete.Command = cmd;
        }
    }
}
