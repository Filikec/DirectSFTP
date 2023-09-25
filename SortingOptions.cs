using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace DirectSFTP
{
    
    public enum SortingStyle
    {
        Ascending,
        Descending
    }
    
    public class SortingOptions<T>
    {
        private ContentPage page;
        public SortingStyle SortingStyle { get; private set; }
        public string CurSorting { get; private set; }
        public Dictionary<string,Tuple<Func<T,IComparable>,MenuFlyoutItem>> SortOptions { get; private set; }
        private MenuBarItem menuItem;
        public EventHandler ChangedOption;

        public SortingOptions(ContentPage page) { 
            this.page = page;
            SortingStyle = SortingStyle.Ascending;
            SortOptions = new();
            CurSorting = null;
            menuItem = new MenuBarItem()
            {
                Text = "Sort by"
            };
        }

        public void Show()
        {
            page.MenuBarItems.Add(menuItem);
        }

        public void AddSortOption(Func<T,IComparable> keySelector, string optionName)
        {
            MenuFlyoutItem newItem = null;
            newItem = new()
            {
                Text = optionName,
                Command = new Command(() => {
                    if (CurSorting != null) {
                        SortOptions[CurSorting].Item2.Text = CurSorting; // remove selected symbol
                    }
                    if (CurSorting == optionName)
                    {
                        if (SortingStyle == SortingStyle.Ascending) SortingStyle = SortingStyle.Descending;
                        else SortingStyle = SortingStyle.Ascending;
                    }
                    newItem.Text = optionName + " " + GetSelectedSymbol();
                    CurSorting = optionName;
                    ChangedOption?.Invoke(this, EventArgs.Empty);
                })
            };

            SortOptions.Add(optionName, new(keySelector,newItem));
            menuItem.Add(newItem);
        }

        public IOrderedEnumerable<T> Sort(IEnumerable<T> items)
        {
            if (SortingStyle == SortingStyle.Ascending)
            {
                return items.OrderBy(SortOptions[CurSorting].Item1);
            }
            else
            {
                return items.OrderByDescending(SortOptions[CurSorting].Item1);
            }
        }

        private char GetSelectedSymbol()
        {
            if (SortingStyle == SortingStyle.Ascending)
            {
                return '▲';
            }
            else
            {
                return '▼';
            }
        }
    }
}
