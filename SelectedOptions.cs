

// Intended to be potentially platform dependent (toolbaritem on Android, menuitem on Windows)
using System.Diagnostics;

namespace DirectSFTP
{
    internal class SelectedOptions
    {
        private ContentPage page;
        public Button download,delete,clear;
        public bool IsShowing { get; private set; }
        private View oldShellTitle;
        public View NewTitleView { get; private set; }

        public SelectedOptions(ContentPage page)
        {
            this.page = page;
            delete = new()
            {
                Text = "Delete",
                Style = page.Resources["buttonSelectionStyle"] as Style
            };
            download = new()
            {
                Text = "Download",
                Style = page.Resources["buttonSelectionStyle"] as Style
            };

            clear = new()
            {
                Text = "Clear Selection",
                Style = page.Resources["buttonSelectionStyle"] as Style
            };

            IsShowing = false;

            NewTitleView = new HorizontalStackLayout()
            {
                delete,
                download,
                clear,
            };
        }

        public void ShowItems()
        {
            oldShellTitle = Shell.GetTitleView(page);
            Shell.SetTitleView(page, NewTitleView);
            IsShowing = true;
        }

        public void HideItems()
        {
            Shell.SetTitleView(page,oldShellTitle);
            IsShowing = false;
        }

        public void SetOnDownload(Command cmd)
        {
            download.Command = cmd;
        }

        public void SetOnDelete(Command cmd)
        {
            delete.Command = cmd;
        }
        public void SetOnClear(Command cmd)
        {
            clear.Released += (a,b) => cmd.Execute(null);
        }
    }
}
