using CairoDesktop.AppGrabber;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CairoDesktop.MenuBar
{
    /// <summary>
    /// Interaction logic for ProgramsMenu.xaml
    /// </summary>
    public partial class ProgramsMenu : UserControl
    {
        public MenuBar MenuBar;

        bool hasLoaded;

        private readonly ObservableCollection<ApplicationInfo> _searchResults = new ObservableCollection<ApplicationInfo>();
        private List<ApplicationInfo> _searchAppsCache;
        private bool _isFetchingSearchCache;

        private const int MAX_SEARCH_RESULTS = 50;

        public ProgramsMenu()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!hasLoaded)
            {
                // Set Programs Menu to use appGrabber's ProgramList as its source
                categorizedProgramsList.ItemsSource = MenuBar._appGrabber.CategoryList;
                searchResultsList.ItemsSource = _searchResults;
                btnClearProgramsSearch.Visibility = Visibility.Hidden;

                // set tab based on user preference
                int i = categorizedProgramsList.Items.IndexOf(MenuBar._appGrabber.CategoryList.GetCategory(Settings.Instance.DefaultProgramsCategory));
                categorizedProgramsList.SelectedIndex = i;

                hasLoaded = true;
            }
        }

        #region Sidebar items
        private void btnAppGrabber_Click(object sender, RoutedEventArgs e)
        {
            if (MenuBar == null)
            {
                return;
            }
            
            MenuBar.ProgramsMenu.IsSubmenuOpen = false;

            // Buttons capture the mouse; need to release so that mouse events go to the intended recipient after closing
            Mouse.Capture(null);

            MenuBar._appGrabber.ShowDialog();
        }

        private void btnUninstallApps_Click(object sender, RoutedEventArgs e)
        {
            // Buttons capture the mouse; need to release so that mouse events go to the intended recipient after closing
            Mouse.Capture(null);

            if (!MenuBar._commandService.InvokeCommand("OpenProgramsControlPanel"))
                CairoMessage.Show(DisplayString.sError_CantOpenAppWiz, DisplayString.sError_OhNo, MessageBoxButton.OK, CairoMessageImage.Error);
        }
        #endregion

        #region Search
        /// <summary>
        /// Focuses the search box and kicks off a refresh of the all-apps cache used to search.
        /// Called by MenuBar when the Programs menu submenu opens.
        /// </summary>
        public void FocusSearchBox()
        {
            RefreshSearchCache();
            FocusSearchTextBox();
        }

        /// <summary>
        /// Clears the search box, returning the menu to the categorized view.
        /// Called by MenuBar when the Programs menu submenu closes.
        /// </summary>
        public void ResetSearch()
        {
            txtProgramsSearch.Clear();
        }

        private void FocusSearchTextBox()
        {
            txtProgramsSearch.Dispatcher.BeginInvoke(new Action(() =>
            {
                txtProgramsSearch.Focusable = true;
                txtProgramsSearch.Focus();
                Keyboard.Focus(txtProgramsSearch);
            }), DispatcherPriority.Render);
        }

        /// <summary>
        /// Refreshes the cache of all apps known to App Grabber (Start Menu + UWP), used as the
        /// search source. This performs a filesystem/registry scan, so it is run off the UI thread
        /// and only triggered once per menu open rather than per keystroke.
        /// </summary>
        private void RefreshSearchCache()
        {
            if (_isFetchingSearchCache || MenuBar?._appGrabber is null)
            {
                return;
            }

            _isFetchingSearchCache = true;

            Task.Run(MenuBar._appGrabber.GetApps).ContinueWith(t =>
            {
                _isFetchingSearchCache = false;

                if (t.IsFaulted)
                {
                    ShellLogger.Warning($"ProgramsMenu: Unable to refresh app search cache: {t.Exception?.GetBaseException().Message}");
                    return;
                }

                _searchAppsCache = t.Result;

                if (!string.IsNullOrEmpty(txtProgramsSearch.Text))
                {
                    RunSearch(txtProgramsSearch.Text);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void RunSearch(string query)
        {
            _searchResults.Clear();

            if (_searchAppsCache is null || MenuBar?._appGrabber is null)
            {
                return;
            }

            IEnumerable<ApplicationInfo> pinned = MenuBar._appGrabber.CategoryList.FlatList;

            List<ApplicationInfo> matches = pinned.Concat(_searchAppsCache)
                .Distinct()
                .Where(app => !string.IsNullOrEmpty(app.Name) && app.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(app => app.Category != null)
                .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MAX_SEARCH_RESULTS)
                .ToList();

            foreach (ApplicationInfo app in matches)
            {
                _searchResults.Add(app);
            }
        }

        private void txtProgramsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = txtProgramsSearch.Text;
            bool isEmpty = string.IsNullOrEmpty(query);

            txtProgramsSearchPlaceholder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

            // SearchTextBoxClearButton's built-in empty-state trigger targets an element named
            // "searchStr" (from the original Search feature), which doesn't exist in this control,
            // so it never fires here; set the button's visibility directly instead.
            btnClearProgramsSearch.Visibility = isEmpty ? Visibility.Hidden : Visibility.Visible;

            if (isEmpty)
            {
                searchResultsList.Visibility = Visibility.Collapsed;
                categorizedProgramsList.Visibility = Visibility.Visible;
                _searchResults.Clear();
                return;
            }

            categorizedProgramsList.Visibility = Visibility.Collapsed;
            searchResultsList.Visibility = Visibility.Visible;

            RunSearch(query);
        }

        private void txtProgramsSearch_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is Key.Return)
            {
                if (_searchResults.Count > 0)
                {
                    MenuBar._appGrabber.LaunchProgram(_searchResults[0]);
                    MenuBar.ProgramsMenu.IsSubmenuOpen = false;
                }

                e.Handled = true;
            }
            else if (e.Key is Key.Escape && !string.IsNullOrEmpty(txtProgramsSearch.Text))
            {
                // clear the search on the first Escape; let a second Escape close the menu as usual
                txtProgramsSearch.Clear();
                e.Handled = true;
            }
        }

        private void btnClearProgramsSearch_Click(object sender, RoutedEventArgs e)
        {
            txtProgramsSearch.Clear();

            FocusSearchTextBox();
        }

        private void ctxSearchResultItem_Opened(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = sender as ContextMenu;
            ApplicationInfo app = menu?.DataContext as ApplicationInfo;

            if (app is null)
            {
                return;
            }

            bool isPinned = app.Category != null;

            foreach (Control item in menu.Items)
            {
                switch (item.Name)
                {
                    case "miSearchResultAdmin":
                        item.Visibility = app.AllowRunAsAdmin ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "miSearchResultAdd":
                    case "sepSearchResultAdd":
                        item.Visibility = isPinned ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    default:
                        break;
                }
            }
        }

        private void searchResults_AddToMenu(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            if (app.IsStoreApp)
            {
                MenuBar._appGrabber.AddStoreApp(app.Target, AppCategoryType.Standard);
            }
            else
            {
                MenuBar._appGrabber.AddByPath(app.Path, AppCategoryType.Standard);
            }

            // re-run the filter so this item now reflects its pinned state
            RunSearch(txtProgramsSearch.Text);
        }
        #endregion

        #region Context menu
        private void ctxProgramsItem_Opened(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = sender as ContextMenu;

            foreach (Control item in menu.Items)
            {
                ApplicationInfo app = item.DataContext as ApplicationInfo;

                switch (item.Name)
                {
                    case "miProgramsItemAdmin":
                        item.Visibility = app.AllowRunAsAdmin ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "miProgramsItemRunAs":
                        item.Visibility = KeyboardUtilities.IsKeyDown(System.Windows.Forms.Keys.ShiftKey) && !app.IsStoreApp ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    default:
                        break;
                }
            }
        }

        private void programsMenu_Open(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.LaunchProgram(app);
        }

        private void programsMenu_OpenAsAdmin(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.LaunchProgramAdmin(app);
        }

        private void programsMenu_OpenRunAs(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.LaunchProgramVerb(app, "runasuser");
        }

        private void programsMenu_AddToQuickLaunch(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.QuickLaunchManager.AddToQuickLaunch(app);
        }

        private void programsMenu_Rename(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.RenameAppDialog(app, (bool? result) =>
            {
                MenuBar.OpenProgramsMenu();
            });
        }

        private void programsMenu_Remove(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.RemoveAppConfirm(app, (bool? result) =>
            {
                MenuBar.OpenProgramsMenu();
            });
        }

        private void programsMenu_Properties(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            ApplicationInfo app = item.DataContext as ApplicationInfo;

            MenuBar._appGrabber.ShowAppProperties(app);
        }

        private void miProgramsChangeCategory_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            ApplicationInfo ai = mi.DataContext as ApplicationInfo;
            mi.Items.Clear();

            // Dynamically add existing categories
            foreach (Category cat in MenuBar._appGrabber.CategoryList)
            {
                if (cat.Type == 0 && cat != ai.Category)
                {
                    MenuItem newItem = new MenuItem();
                    newItem.Header = cat.DisplayName;

                    object[] appNewCat = new object[] { ai, cat };
                    newItem.DataContext = appNewCat;

                    newItem.Click += new RoutedEventHandler(miProgramsChangeCategory_Click);
                    mi.Items.Add(newItem);
                }
            }

            // Add separated option to add new category
            if (mi.Items.Count > 0)
            {
                mi.Items.Add(new Separator());
            }

            MenuItem addCategoryItem = new MenuItem();
            addCategoryItem.Header = DisplayString.sProgramsMenu_AddToNewCategory;
            addCategoryItem.Click += miProgramsAddCategory_Click;
            addCategoryItem.DataContext = ai;

            mi.Items.Add(addCategoryItem);
        }

        private void miProgramsChangeCategory_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            object[] appNewCat = mi.DataContext as object[];
            ApplicationInfo ai = appNewCat[0] as ApplicationInfo;
            Category newCat = appNewCat[1] as Category;

            ai.Category.Remove(ai);
            newCat.Add(ai);

            MenuBar._appGrabber.Save();
        }

        private void miProgramsAddCategory_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            ApplicationInfo ai = mi.DataContext as ApplicationInfo;

            Common.MessageControls.Input inputControl = new Common.MessageControls.Input();
            inputControl.Initialize(DisplayString.sAppGrabber_Untitled);

            CairoMessage.ShowControl(DisplayString.sProgramsMenu_AddCategoryInfo,
                DisplayString.sProgramsMenu_AddCategoryTitle,
                CairoMessageImage.Default,
                inputControl,
                DisplayString.sInterface_OK,
                DisplayString.sInterface_Cancel,
                (bool? result) => {
                    if (result == true)
                    {
                        Category newCat = new Category(inputControl.Text);
                        MenuBar._appGrabber.CategoryList.Add(newCat);

                        ai.Category.Remove(ai);
                        newCat.Add(ai);

                        MenuBar._appGrabber.Save();
                    }

                    MenuBar.OpenProgramsMenu();
                });
        }
        #endregion

        #region Category context menu
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = sender as ContextMenu;
            Category category = menu.DataContext as Category;

            bool enableItems = category.Type <= 0;

            foreach (Control item in menu.Items)
            {
                item.IsEnabled = enableItems;

                if (item is MenuItem mi)
                {
                    if ((string)mi.CommandParameter == "MoveUp" && category.ParentCategoryList.IndexOf(category) <= CategoryList.MIN_CATEGORIES)
                    {
                        item.IsEnabled = false;
                    }
                    else if ((string)mi.CommandParameter == "MoveDown" && category.ParentCategoryList.IndexOf(category) >= category.ParentCategoryList.Count - 1)
                    {
                        item.IsEnabled = false;
                    }
                }
            }
        }
        
        private void categoryMenu_Rename(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            Category category = menuItem.DataContext as Category;

            Common.MessageControls.Input inputControl = new Common.MessageControls.Input();
            inputControl.Initialize(category.Name);

            CairoMessage.ShowControl(string.Format(DisplayString.sProgramsMenu_RenameCategoryInfo, category.DisplayName),
                string.Format(DisplayString.sProgramsMenu_RenameTitle, category.DisplayName),
                CairoMessageImage.Default,
                inputControl,
                DisplayString.sInterface_Rename,
                DisplayString.sInterface_Cancel,
                (bool? result) => {
                    if (result == true)
                    {
                        category.Name = inputControl.Text;
                        MenuBar._appGrabber.Save();
                    }

                    MenuBar.OpenProgramsMenu();
                });
        }

        private void categoryMenu_Delete(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            Category category = menuItem.DataContext as Category;
            CategoryList catList = category.ParentCategoryList;

            CairoMessage.ShowOkCancel(string.Format(DisplayString.sProgramsMenu_DeleteCategoryInfo, category.DisplayName, MenuBar._appGrabber.CategoryList.GetSpecialCategory(AppCategoryType.All)),
                string.Format(DisplayString.sProgramsMenu_DeleteCategoryTitle, category.DisplayName),
                CairoMessageImage.Warning,
                DisplayString.sInterface_Yes,
                DisplayString.sInterface_No,
                (bool? result) => {
                    if (result == true)
                    {
                        catList.Remove(category);
                        MenuBar._appGrabber.Save();
                    }

                    MenuBar.OpenProgramsMenu();
                });
        }

        private void categoryMenu_MoveUp(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            Category category = menuItem.DataContext as Category;
            CategoryList catList = category.ParentCategoryList;

            catList.MoveCategory(category, -1);
            MenuBar._appGrabber.Save();
        }

        private void categoryMenu_MoveDown(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            Category category = menuItem.DataContext as Category;
            CategoryList catList = category.ParentCategoryList;

            catList.MoveCategory(category, 1);
            MenuBar._appGrabber.Save();
        }
        #endregion
    }
}
