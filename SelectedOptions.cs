

// Intended to be potentially platform dependent (toolbaritem on Android, menuitem on Windows)
using System.Diagnostics;

namespace DirectSFTP
{
    internal class SelectedOptions
    {
        public Button download,delete,clear,rename;
        public bool IsShowing { get; private set; }
        private Page page;
        private FlexLayout layout;
        private List<View> oldViews;

        public SelectedOptions(ContentPage page, FlexLayout layout, Command onClear)
        {
            this.page = page;
            delete = new()
            {
                Text = "Delete",
                Style = page.Resources["buttonDeleteStyle"] as Style
            };

            download = new()
            {
                Text = "Download",
                Style = page.Resources["buttonSelectionStyle"] as Style
            };

            clear = new()
            {
                Text = "Clear Selection",
                Style = page.Resources["buttonSelectionStyle"] as Style,
            };
            rename = new()
            {
                Text = "Rename",
                Style = page.Resources["buttonSelectionStyle"] as Style,
            };

            clear.Released += (a, b) => onClear.Execute(null);
            IsShowing = false;
            this.layout = layout;
            oldViews = new();

            foreach (View oldView in layout.Children)
            {
                oldViews.Add(oldView);
            }

        }

        public void ShowItems()
        {
            layout.Clear();
            layout.Add(clear);
            layout.Add(download);
            layout.Add(rename);
            layout.Add(delete);
            IsShowing = true;
        }

        public void HideItems()
        {
            layout.Clear();
            foreach (View oldView in oldViews)
            {
                layout.Add(oldView);
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

        public void SetOnRename(Command cmd)
        {
            rename.Command = cmd;
        }

        public void ShowRename(bool show)
        {
            page.Dispatcher.Dispatch(() =>
            {
                rename.IsVisible = show;
            });
        }
    }
}
