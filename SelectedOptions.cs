

// Intended to be potentially platform dependent (toolbaritem on Android, menuitem on Windows)
using System.Diagnostics;

namespace DirectSFTP
{
    internal class SelectedOptions
    {
        private ContentPage page;
        public Button download,delete,clear;
        public bool IsShowing { get; private set; }
        private HorizontalStackLayout stack;
        private List<View> oldViews;

        public SelectedOptions(ContentPage page, HorizontalStackLayout stack)
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

            this.stack = stack;
            oldViews = new();

            foreach (View oldView in stack.Children)
            {
                oldViews.Add(oldView);
            }
        }

        public void ShowItems()
        {
            stack.Clear();
            stack.Add(download);
            stack.Add(clear);
            stack.Add(delete);
            IsShowing = true;
        }

        public void HideItems()
        {
            stack.Clear();
            foreach (View oldView in oldViews)
            {
                stack.Add(oldView);
            }
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
