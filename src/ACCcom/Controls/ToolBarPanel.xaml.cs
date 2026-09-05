using System.Windows.Controls;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.ViewModels;

namespace ACCcom.Controls;

public partial class ToolBarPanel : UserControl
{
    public ToolBarPanel()
    {
        InitializeComponent();
    }

    /// <summary>Opens the bookmark list as a menu: left-click jumps to the
    /// bookmark's entry, right-click offers delete. Built in code so each item
    /// can carry both actions with a stable command parameter (the bookmark).</summary>
    private void BookmarkListButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        var menu = new ContextMenu();
        foreach (var bm in vm.Bookmarks)
        {
            var item = new MenuItem
            {
                Header = $"{bm.Label}  {bm.Preview}",
                ToolTip = $"{bm.Direction} #{bm.EntryId}",
                Command = vm.JumpToBookmarkCommand,
                CommandParameter = bm
            };

            var delete = new MenuItem
            {
                Header = LanguageManager.Instance["Menu.DeleteBookmark"],
                Command = vm.RemoveBookmarkCommand,
                CommandParameter = bm
            };
            item.ContextMenu = new ContextMenu { Items = { delete } };

            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = LanguageManager.Instance["Menu.NoBookmarks"], IsEnabled = false });
        }

        menu.PlacementTarget = BookmarkListButton;
        menu.IsOpen = true;
    }
}
