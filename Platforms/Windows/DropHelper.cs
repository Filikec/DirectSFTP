using System.Diagnostics;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using DragEventArgs = Microsoft.UI.Xaml.DragEventArgs;
using DragStartingEventArgs = Microsoft.UI.Xaml.DragStartingEventArgs;

namespace DirectSFTP.Platforms.Windows
{
    public static class DropHelper
    {
        private static readonly Dictionary<UIElement, TypedEventHandler<UIElement, DragStartingEventArgs>> DragStartingEventHandlers = new();
        private static readonly Dictionary<UIElement, DragEventHandler> DragEventHandlers = new();

        public static void RegisterDrag(UIElement element)
        {
            element.CanDrag = true;
            element.DragStarting += (a,b) => { Debug.WriteLine("started dragging"); };
        }

        public static void RegisterDrop(UIElement element, Action<DragEventArgs> onDrop)
        {
            element.AllowDrop = true;
            element.Drop += (a,b) => { 
                onDrop(b);
            };
            element.DragOver += OnDragOver;
        }


        private static void OnDragOver(object sender, DragEventArgs e)
        {

            e.AcceptedOperation = DataPackageOperation.Copy;
            
        }
    }
}
