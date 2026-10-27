using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Input;

namespace Typify
{
    public partial class MainWindow : Window
    {

        private string _workingDirectory = string.Empty;
        private const string GitTabMarker = "GitTab";
        private int _untitledDocumentIndex = 1;
        private bool _isClosing;

        private sealed class DocumentTabState
        {
            public string FilePath;
            public string UntitledName;
            public TypifyDocumentView View;
            public TabItem Tab;
            public string InitialText;
            public bool IsPlaceholderTab;
        }
        private bool _isDarkTheme
        {
            get => Properties.Settings.Default.IsDarkTheme;
            set
            {
                Properties.Settings.Default.IsDarkTheme = value;
                Properties.Settings.Default.Save();
            }
        }
        private bool _isLivePreviewEnabled;
        private bool _enableWordWrap;
        private bool _enableAutoSave;
        private bool _showStatusBar;
        private bool _confirmBeforeClosing;
        private int _autoSaveIntervalMinutes;
        private double _uiScale;
        private int _editorTabSize;
        private string _editorFontFamily;

        public static Geometry DarkModeGeometry { get; } = Geometry.Parse("F1 M12,22 C17.5228475,22 22,17.5228475 22,12 C22,6.4771525 17.5228475,2 12,2 C6.4771525,2 2,6.4771525 2,12 C2,17.5228475 6.4771525,22 12,22 Z M12,20.5 L12,3.5 C16.6944204,3.5 20.5,7.30557963 20.5,12 C20.5,16.6944204 16.6944204,20.5 12,20.5 Z");
        public static Geometry GitGeometry { get; } = Geometry.Parse("F1 M5,58c-2.76142,0 -5,-2.23858 -5,-5v-48c0,-2.76142 2.23858,-5 5,-5h33v12.54404c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,0.73514 0.13221,1.43941 0.37415,2.09031l-15.28384,15.28384c-0.6509,-0.24194 -1.35517,-0.37415 -2.09031,-0.37415c-3.31371,0 -6,2.68629 -6,6c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-0.73514 -0.13221,-1.43941 -0.37415,-2.09031l14.87415,-14.87415l0,11.50851c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.08808c2.06553,-0.94801 3.5,-3.03446 3.5,-5.45596c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.54404h10c2.76142,0 5,2.23858 5,5v48c0,2.76142 -2.23858,5 -5,5z");
        public static Geometry SaveGeometry { get; } = Geometry.Parse("F1 M402.115,84.008L323.088,4.981C319.899,1.792,315.574,0,311.063,0H17.005C7.613,0,0,7.614,0,17.005v373.086 c0,9.392,7.613,17.005,17.005,17.005h373.086c9.392,0,17.005-7.613,17.005-17.005V96.032 C407.096,91.523,405.305,87.197,402.115,84.008z M300.664,163.567H67.129V38.862h233.535V163.567z M214.051,148.16h43.08c3.131,0,5.668-2.538,5.668-5.669V59.584c0-3.13-2.537-5.668-5.668-5.668h-43.08 c-3.131,0-5.668,2.538-5.668,5.668v82.907C208.383,145.622,210.92,148.16,214.051,148.16z");
        public static Geometry PrintGeometry { get; } = Geometry.Parse("M390.91,94.277h-64.25v-72.23c0-9.496-7.697-17.193-17.191-17.193H103.138c-9.495,0-17.192,7.697-17.192,17.193v72.23 h-64.25C9.713,94.277,0,103.99,0,115.972v159.834c0,11.981,9.713,21.696,21.696,21.696h20.655 c11.982,0,21.696-9.715,21.696-21.696v-30.853h21.899v145.605c0,9.496,7.697,17.193,17.192,17.193h206.331 c9.494,0,17.191-7.697,17.191-17.193V244.953h21.898v30.853c0,11.981,9.713,21.696,21.697,21.696h20.654 c11.982,0,21.695-9.715,21.695-21.696V115.972C412.605,103.99,402.893,94.277,390.91,94.277z M120.33,39.238h171.945v55.039 H120.33V39.238z M292.275,373.367H120.33V244.953h171.945V373.367L292.275,373.367z M160.018,287.429h92.57c5.992,0,10.848-4.856,10.848-10.849c0-5.99-4.855-10.848-10.848-10.848h-92.57 c-5.991,0-10.848,4.857-10.848,10.848C149.17,282.572,154.027,287.429,160.018,287.429z M252.588,318.527h-92.57c-5.991,0-10.848,4.856-10.848,10.848c0,5.99,4.857,10.849,10.848,10.849h92.57 c5.992,0,10.848-4.856,10.848-10.849C263.436,323.384,258.58,318.527,252.588,318.527z");
        public static Geometry UndoGeometry { get; } = Geometry.Parse("F0 M20 7H9.00001C7.13077 7 6.19615 7 5.5 7.40193C5.04395 7.66523 4.66524 8.04394 4.40193 8.49999C4 9.19615 4 10.1308 4 12C4 13.8692 4 14.8038 4.40192 15.5C4.66523 15.9561 5.04394 16.3348 5.5 16.5981C6.19615 17 7.13077 17 9 17H16M20 7L17 4M20 7L17 10");
        public static Geometry CopyGeometry { get; } = Geometry.Parse("M21 8C21 6.34315 19.6569 5 18 5H10C8.34315 5 7 6.34315 7 8V20C7 21.6569 8.34315 23 10 23H18C19.6569 23 21 21.6569 21 20V8ZM19 8C19 7.44772 18.5523 7 18 7H10C9.44772 7 9 7.44772 9 8V20C9 20.5523 9.44772 21 10 21H18C18.5523 21 19 20.5523 19 20V8Z M6 3H16C16.5523 3 17 2.55228 17 2C17 1.44772 16.5523 1 16 1H6C4.34315 1 3 2.34315 3 4V18C3 18.5523 3.44772 19 4 19C4.55228 19 5 18.5523 5 18V4C5 3.44772 5.44772 3 6 3Z");
        public static Geometry PasteGeometry { get; } = Geometry.Parse("F1 M12 0C11.2347 0 10.6293 0.125708 10.1567 0.359214C9.9845 0.44429 9.82065 0.544674 9.68861 0.62717L9.59036 0.688808C9.49144 0.751003 9.4082 0.803334 9.32081 0.853848C9.09464 0.984584 9.00895 0.998492 9.00053 0.999859C8.99983 0.999973 9.00019 0.999859 9.00053 0.999859C7.89596 0.999859 7 1.89543 7 3H6C4.34315 3 3 4.34315 3 6V20C3 21.6569 4.34315 23 6 23H18C19.6569 23 21 21.6569 21 20V6C21 4.34315 19.6569 3 18 3H17C17 1.89543 16.1046 1 15 1C15.0003 1 15.0007 1.00011 15 1C14.9916 0.998633 14.9054 0.984584 14.6792 0.853848C14.5918 0.80333 14.5086 0.751004 14.4096 0.688804L14.3114 0.62717C14.1793 0.544674 14.0155 0.44429 13.8433 0.359214C13.3707 0.125708 12.7653 0 12 0ZM16.7324 5C16.3866 5.5978 15.7403 6 15 6H9C8.25972 6 7.61337 5.5978 7.26756 5H6C5.44772 5 5 5.44772 5 6V20C5 20.5523 5.44772 21 6 21H18C18.5523 21 19 20.5523 19 20V6C19 5.44772 18.5523 5 18 5H16.7324ZM11.0426 2.15229C11.1626 2.09301 11.4425 2 12 2C12.5575 2 12.8374 2.09301 12.9574 2.15229C13.0328 2.18953 13.1236 2.24334 13.2516 2.32333L13.3261 2.37008C13.43 2.43542 13.5553 2.51428 13.6783 2.58539C13.9712 2.75469 14.4433 3 15 3V4H9V3C9.55666 3 10.0288 2.75469 10.3217 2.58539C10.4447 2.51428 10.57 2.43543 10.6739 2.37008L10.7484 2.32333C10.8764 2.24334 10.9672 2.18953 11.0426 2.15229Z");
        public static Geometry CutGeometry { get; } = Geometry.Parse("F1 M5.36676 3.22604C5.79421 2.87631 6.42423 2.93932 6.77396 3.36676L12 9.75415L17.226 3.36676C17.5758 2.93932 18.2058 2.87631 18.6332 3.22604C19.0607 3.57577 19.1237 4.20579 18.774 4.63324L13.2921 11.3333L15.4739 14H16.5C18.433 14 20 15.567 20 17.5V18C20 19.6569 18.6569 21 17 21C15.3431 21 14 19.6569 14 18V15.357L12 12.9125L10 15.357V18C10 19.6569 8.65685 21 7 21C5.34315 21 4 19.6569 4 18V17.5C4 15.567 5.567 14 7.5 14H8.52612L10.7079 11.3333L5.22604 4.63324C4.87632 4.20579 4.93932 3.57577 5.36676 3.22604ZM8 16H7.5C6.67157 16 6 16.6716 6 17.5V18C6 18.5523 6.44772 19 7 19C7.55228 19 8 18.5523 8 18V16ZM16 16V18C16 18.5523 16.4477 19 17 19C17.5523 19 18 18.5523 18 18V17.5C18 16.6716 17.3284 16 16.5 16H16Z");

        public MainWindow()
        {
            InitializeComponent();
            UpdateWindowStateControls();
            KeyDown += MainWindow_KeyDown;
            MainTabs.SelectionChanged += MainTabs_SelectionChanged;
            ExplorerOverlay.FileOpenRequested += ExplorerOverlay_FileOpenRequested;
            ExplorerOverlay.DockToggleRequested += ExplorerOverlay_DockToggleRequested;
            ExplorerOverlay.CloseDockedRequested += ExplorerOverlay_CloseDockedRequested;
            CommitOverlay.DockToggleRequested += CommitOverlay_DockToggleRequested;
            CommitOverlay.CloseDockedRequested += CommitOverlay_CloseDockedRequested;

            _isLivePreviewEnabled = Properties.Settings.Default.IsLivePreviewEnabled;
            _enableWordWrap = Properties.Settings.Default.EnableWordWrap;
            _enableAutoSave = Properties.Settings.Default.EnableAutoSave;
            _showStatusBar = Properties.Settings.Default.ShowStatusBar;
            _confirmBeforeClosing = Properties.Settings.Default.ConfirmBeforeClosing;
            _autoSaveIntervalMinutes = Properties.Settings.Default.AutoSaveIntervalMinutes;
            _uiScale = Properties.Settings.Default.UiScale <= 0 ? 1.0 : Properties.Settings.Default.UiScale;
            _editorTabSize = Properties.Settings.Default.EditorTabSize <= 0 ? 2 : Properties.Settings.Default.EditorTabSize;
            _editorFontFamily = string.IsNullOrWhiteSpace(Properties.Settings.Default.EditorFontFamily)
                ? "Consolas"
                : Properties.Settings.Default.EditorFontFamily;

            StatusText.Visibility = _showStatusBar ? Visibility.Visible : Visibility.Collapsed;
            CreateAndSelectDocumentTab(null, "= Hello, Typst\n\nWrite Typst source here.\n\n#let answer = 42\nThe answer is *#answer*.");
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var state = GetActiveDocumentState();
            if (state == null) return;

            string sourceDirectory = state.FilePath != null
                ? Path.GetDirectoryName(state.FilePath) ?? string.Empty
                : _workingDirectory;
            state.View.SetSourceContext(_workingDirectory, sourceDirectory);
            state.View.FocusEditor();
        }

        private DocumentTabState GetActiveDocumentState()
        {
            var tab = MainTabs.SelectedItem as TabItem;
            return tab != null ? tab.Tag as DocumentTabState : null;
        }

        private TypifyDocumentView GetActiveDocumentView()
        {
            DocumentTabState state = GetActiveDocumentState();
            return state != null ? state.View : null;
        }

        private GitTabView GetOpenGitTabView()
        {
            TabItem gitTab = MainTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, GitTabMarker, StringComparison.Ordinal));
            return gitTab != null ? gitTab.Content as GitTabView : null;
        }

        private async Task OpenOrActivateGitTabAsync()
        {
            TabItem gitTab = MainTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, GitTabMarker, StringComparison.Ordinal));
            if (gitTab != null)
            {
                MainTabs.SelectedItem = gitTab;
                return;
            }

            var gitView = new GitTabView();
            gitView.RepositoryRefreshed += GitTabView_RepositoryRefreshed;
            gitView.UpdateTheme(_isDarkTheme);

            gitTab = new TabItem
            {
                Header = "Git",
                Content = gitView,
                Tag = GitTabMarker
            };

            MainTabs.Items.Add(gitTab);
            MainTabs.SelectedItem = gitTab;
            await gitView.InitializeAsync(_workingDirectory, _isDarkTheme);
        }

        private async void OpenGitTabMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await OpenOrActivateGitTabAsync();
        }

        private TabItem FindDocumentTabByPath(string fullPath)
        {
            return MainTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item =>
                {
                    var state = item.Tag as DocumentTabState;
                    return state != null && state.FilePath != null &&
                           string.Equals(state.FilePath, fullPath, StringComparison.OrdinalIgnoreCase);
                });
        }

        private TabItem FindReusableUntitledDocumentTab()
        {
            return MainTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item =>
                {
                    var state = item.Tag as DocumentTabState;
                    if (state == null || state.FilePath != null || state.View == null)
                        return false;

                    if (state.IsPlaceholderTab)
                        return true;

                    string currentText = state.View.DocumentText ?? string.Empty;
                    string initialText = state.InitialText ?? string.Empty;
                    return string.Equals(currentText, initialText, StringComparison.Ordinal);
                });
        }

        private void CreateAndSelectDocumentTab(string filePath, string initialText)
        {
            string fullPath = string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFullPath(filePath);
            if (fullPath != null)
            {
                TabItem existingTab = FindDocumentTabByPath(fullPath);
                if (existingTab != null)
                {
                    MainTabs.SelectedItem = existingTab;
                    return;
                }
            }

            TypifyDocumentView view;
            TabItem tab;
            DocumentTabState state;

            if (fullPath != null)
            {
                TabItem reusableTab = FindReusableUntitledDocumentTab();
                state = reusableTab != null ? reusableTab.Tag as DocumentTabState : null;
                if (state != null && state.View != null)
                {
                    tab = reusableTab;
                    view = state.View;
                }
                else
                {
                    view = new TypifyDocumentView();
                    view.SetDarkTheme(_isDarkTheme);
                    view.SetLivePreviewEnabled(_isLivePreviewEnabled);

                    tab = new TabItem();
                    state = new DocumentTabState
                    {
                        View = view,
                        Tab = tab
                    };

                    tab.Tag = state;
                    tab.Content = view;
                    MainTabs.Items.Add(tab);
                }
            }
            else
            {
                view = new TypifyDocumentView();
                view.SetDarkTheme(_isDarkTheme);
                view.SetLivePreviewEnabled(_isLivePreviewEnabled);

                tab = new TabItem();
                state = new DocumentTabState
                {
                    View = view,
                    Tab = tab
                };

                tab.Tag = state;
                tab.Content = view;
                MainTabs.Items.Add(tab);
            }

            state.FilePath = fullPath;
            state.UntitledName = fullPath == null ? "Untitled " + _untitledDocumentIndex++ : null;

            if (fullPath != null)
            {
                string fileText = File.ReadAllText(fullPath);
                view.DocumentText = fileText;
                state.InitialText = fileText;
                state.IsPlaceholderTab = false;
                string sourceDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                _workingDirectory = sourceDirectory;
                ExplorerOverlay.SetWorkingDirectory(_workingDirectory);
                view.SetSourceContext(_workingDirectory, sourceDirectory);
            }
            else
            {
                string text = initialText ?? string.Empty;
                view.DocumentText = text;
                state.InitialText = text;
                state.IsPlaceholderTab = true;
                view.SetSourceContext(_workingDirectory, _workingDirectory);
            }

            tab.Header = CreateDocumentTabHeader(state);
            MainTabs.SelectedItem = tab;
            view.FocusEditor();
        }

        private object CreateDocumentTabHeader(DocumentTabState state)
        {
            var dock = new DockPanel { LastChildFill = false };
            var title = new TextBlock
            {
                Text = GetDocumentTitle(state),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var closeButton = new Button
            {
                Content = "×",
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.Gray,
                Cursor = Cursors.Hand,
                Tag = state.Tab,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeButton.Click += DocumentTabCloseButton_Click;

            DockPanel.SetDock(closeButton, Dock.Right);
            dock.Children.Add(closeButton);
            dock.Children.Add(title);
            return dock;
        }

        private string GetDocumentTitle(DocumentTabState state)
        {
            if (!string.IsNullOrWhiteSpace(state.FilePath))
                return Path.GetFileName(state.FilePath);
            return string.IsNullOrWhiteSpace(state.UntitledName) ? "Untitled" : state.UntitledName;
        }

        private void RefreshDocumentTabHeader(DocumentTabState state)
        {
            state.Tab.Header = CreateDocumentTabHeader(state);
        }

        private void DocumentTabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tab = button != null ? button.Tag as TabItem : null;
            if (tab == null) return;

            CloseDocumentTab(tab);
        }

        private void CloseDocumentTab(TabItem tab)
        {
            var state = tab.Tag as DocumentTabState;
            if (state != null) state.View.Shutdown();
            MainTabs.Items.Remove(tab);
            if (MainTabs.Items.Count == 0)
                CreateAndSelectDocumentTab(null, string.Empty);
        }

        private bool SaveDocumentToPath(DocumentTabState state, string filePath)
        {
            try
            {
                File.WriteAllText(filePath, state.View.DocumentText);
                state.FilePath = Path.GetFullPath(filePath);
                state.InitialText = state.View.DocumentText ?? string.Empty;
                state.IsPlaceholderTab = false;
                string sourceDirectory = Path.GetDirectoryName(state.FilePath) ?? string.Empty;
                _workingDirectory = sourceDirectory;
                ExplorerOverlay.SetWorkingDirectory(_workingDirectory);
                state.View.SetSourceContext(_workingDirectory, sourceDirectory);
                RefreshDocumentTabHeader(state);
                StatusText.Text = "Saved: " + state.FilePath;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error saving file", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to save file.";
                return false;
            }
        }

        private void ExplorerOverlay_DockToggleRequested(object sender, EventArgs e)
        {
            if (ExplorerOverlay.IsDocked)
                UndockOverlay(ExplorerOverlay, ExplorerOverlay.SetDocked);
            else
                DockOverlay(ExplorerOverlay, "Explorer", ExplorerOverlay.SetDocked);
        }

        private void CommitOverlay_DockToggleRequested(object sender, EventArgs e)
        {
            if (CommitOverlay.IsDocked)
                UndockOverlay(CommitOverlay, CommitOverlay.SetDocked);
            else
                DockOverlay(CommitOverlay, "Git Changes", CommitOverlay.SetDocked);
        }

        private void ExplorerOverlay_CloseDockedRequested(object sender, EventArgs e)
        {
            UndockOverlay(ExplorerOverlay, ExplorerOverlay.SetDocked);
            ExplorerOverlay.ToggleVisibility();
        }

        private void CommitOverlay_CloseDockedRequested(object sender, EventArgs e)
        {
            UndockOverlay(CommitOverlay, CommitOverlay.SetDocked);
            CommitOverlay.ToggleVisibility();
        }

        private void DockOverlay(UserControl overlay, string title, Action<bool> setDocked)
        {
            setDocked(true);
            OverlayHost.Children.Remove(overlay);

            var tab = new TabItem { Header = title, Content = overlay };
            dockedOverlayTabs.Items.Add(tab);
            dockedOverlayTabs.Visibility = Visibility.Visible;
            dockedOverlayTabs.SelectedItem = tab;
            UpdateDockedOverlayVisibility();
        }

        private void UndockOverlay(UserControl overlay, Action<bool> setDocked)
        {
            TabItem tab = dockedOverlayTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Content, overlay));
            if (tab == null) return;

            tab.Content = null;
            dockedOverlayTabs.Items.Remove(tab);
            if (dockedOverlayTabs.Items.Count == 0)
                dockedOverlayTabs.Visibility = Visibility.Collapsed;

            OverlayHost.Children.Add(overlay);
            Grid.SetColumn(overlay, 1);
            Panel.SetZIndex(overlay, 100);
            setDocked(false);
            UpdateDockedOverlayVisibility();
        }

        private void UpdateDockedOverlayVisibility()
        {
            bool hasDockedOverlays = dockedOverlayTabs.Items.Count > 0;
            SplitterGrid.Width = new GridLength(hasDockedOverlays ? 5 : 0);
            DockedOverlaySplitter.Visibility = hasDockedOverlays ? Visibility.Visible : Visibility.Collapsed;

            dockedOverlayTabs.ApplyTemplate();
            var headerPanel = dockedOverlayTabs.Template.FindName("HeaderPanel", dockedOverlayTabs) as TabPanel;
            if (headerPanel != null)
            {
                headerPanel.Visibility = dockedOverlayTabs.Items.Count > 1
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            LeftExplorerTab.Visibility = ExplorerOverlay.IsDocked ? Visibility.Collapsed : Visibility.Visible;
            LeftGitChangesTab.Visibility = CommitOverlay.IsDocked ? Visibility.Collapsed : Visibility.Visible;
            LeftToolBar.Visibility = LeftExplorerTab.Visibility == Visibility.Visible ||
                                     LeftGitChangesTab.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SelectDockedOverlay(UserControl overlay)
        {
            TabItem tab = dockedOverlayTabs.Items.OfType<TabItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Content, overlay));
            if (tab != null) dockedOverlayTabs.SelectedItem = tab;
        }

        private void ActivateExplorerOverlay()
        {
            if (ExplorerOverlay.IsDocked)
            {
                SelectDockedOverlay(ExplorerOverlay);
                ExplorerOverlay.ShowAndFocus();
            }
            else
            {
                ExplorerOverlay.ToggleVisibility();
            }

            if (!CommitOverlay.IsDocked && CommitOverlay.IsOpen)
                CommitOverlay.ToggleVisibility();
        }

        private void ActivateCommitOverlay()
        {
            if (CommitOverlay.IsDocked)
            {
                SelectDockedOverlay(CommitOverlay);
                CommitOverlay.ShowAndFocus();
            }
            else
            {
                CommitOverlay.ToggleVisibility();
            }

            if (!ExplorerOverlay.IsDocked && ExplorerOverlay.IsOpen)
                ExplorerOverlay.ToggleVisibility();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyTheme(_isDarkTheme);
            await CommitOverlay.InitializeAsync(_workingDirectory, _isDarkTheme);
            CommitOverlay.CommitCompleted += CommitOverlay_CommitCompleted;
            CommitOverlay.VisibilityChanged += (s, ev) => UpdateLeftTabState();
            CommitOverlay.StatusUpdated += (s, ev) => UpdateLeftTabState();
            UpdateLeftTabState();
        }

        private void LeftGitChangesTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            switch (((Border)sender).Name)
            {
                case "LeftGitChangesTab":
                    ActivateCommitOverlay();
                    UpdateLeftTabState();
                    break;
                case "LeftExplorerTab":
                    ActivateExplorerOverlay();
                    break;
            }
        }

        private void LeftGitChangesTab_MouseEnter(object sender, MouseEventArgs e)
        {
            if (((Border)sender).Name == "LeftGitChangesTab")
            {
                if (!CommitOverlay.IsOpen)
                    LeftGitChangesTab.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            }
            else if (((Border)sender).Name == "LeftExplorerTab")
            {
                if (!ExplorerOverlay.IsOpen)
                    LeftExplorerTab.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            }
        }

        private void LeftGitChangesTab_MouseLeave(object sender, MouseEventArgs e)
        {
            if  (((Border)sender).Name == "LeftExplorerTab")
            {
                if (!ExplorerOverlay.IsOpen)
                    LeftExplorerTab.Background = Brushes.Transparent;
                return;
            }
            UpdateLeftTabState();
        }

        private void UpdateLeftTabState()
        {
            if (CommitOverlay.IsOpen)
                LeftGitChangesTab.SetResourceReference(Border.BackgroundProperty, "SelectedBrush");
            else
                LeftGitChangesTab.Background = Brushes.Transparent;

            int count = CommitOverlay.StagedCount;
            LeftGitChangesBadgeText.Text = count.ToString();
            LeftGitChangesBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CommitOverlay_CommitCompleted(object sender, EventArgs e)
        {
            GitTabView gitTabView = GetOpenGitTabView();
            if (gitTabView != null)
                await gitTabView.RefreshAllAsync();

            StatusText.Text = "Commit completed successfully.";
            UpdateLeftTabState();
        }

        private async void GitTabView_RepositoryRefreshed(object sender, EventArgs e)
        {
            await CommitOverlay.RefreshStatusAsync();
            UpdateLeftTabState();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (ctrl && shift && e.Key == Key.C)
            {
                e.Handled = true;
                ActivateCommitOverlay();
                UpdateLeftTabState();
                return;
            }

            if (ctrl && shift && e.Key == Key.S)
            {
                e.Handled = true;
                SaveActiveDocumentAs();
                return;
            }

            if (ctrl && shift && e.Key == Key.Tab)
            {
                e.Handled = true;
                SelectAdjacentTab(-1);
                return;
            }

            if (ctrl && e.Key == Key.N)
            {
                e.Handled = true;
                CreateAndSelectDocumentTab(null, string.Empty);
                StatusText.Text = "New document created.";
                return;
            }

            if (ctrl && e.Key == Key.O)
            {
                e.Handled = true;
                OpenTypstFileWithDialog();
                return;
            }

            if (ctrl && e.Key == Key.S)
            {
                e.Handled = true;
                SaveActiveDocument();
                return;
            }

            if (ctrl && e.Key == Key.P)
            {
                e.Handled = true;
                _ = PrintDocumentAsync();
                return;
            }

            if (ctrl && (e.Key == Key.W || e.Key == Key.F4))
            {
                e.Handled = true;
                CloseActiveDocumentTab();
                return;
            }

            if (ctrl && e.Key == Key.Tab)
            {
                e.Handled = true;
                SelectAdjacentTab(1);
                return;
            }

            if (ctrl && e.Key == Key.Oem3)
            {
                e.Handled = true;
                ActivateExplorerOverlay();
                UpdateLeftTabState();
                return;
            }

            if (e.Key == Key.F5)
            {
                e.Handled = true;
                ToggleLivePreview();
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void LivePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleLivePreview();
        }

        private void ApplyLivePreviewToAllDocuments()
        {
            foreach (TabItem tab in MainTabs.Items.OfType<TabItem>())
            {
                var state = tab.Tag as DocumentTabState;
                if (state != null)
                    state.View.SetLivePreviewEnabled(_isLivePreviewEnabled);
            }
        }

        private void ShowPreferencesWindow()
        {
            var dialog = new PreferencesWindow(
                _isDarkTheme,
                _isLivePreviewEnabled,
                _enableWordWrap,
                _enableAutoSave,
                _showStatusBar,
                _confirmBeforeClosing,
                _autoSaveIntervalMinutes,
                _uiScale,
                _editorTabSize,
                _editorFontFamily)
            {
                Owner = this
            };

            void ApplyPreferences(object sender, EventArgs args)
            {
                if (_isDarkTheme != dialog.IsDarkThemeSelected)
                {
                    _isDarkTheme = dialog.IsDarkThemeSelected;
                    ApplyTheme(_isDarkTheme);
                }

                if (_isLivePreviewEnabled != dialog.IsLivePreviewSelected)
                {
                    _isLivePreviewEnabled = dialog.IsLivePreviewSelected;
                    ApplyLivePreviewToAllDocuments();
                    Properties.Settings.Default.IsLivePreviewEnabled = _isLivePreviewEnabled;
                }

                _enableWordWrap = dialog.EnableWordWrapSelected;
                _enableAutoSave = dialog.AutoSaveSelected;
                _showStatusBar = dialog.ShowStatusBarSelected;
                _confirmBeforeClosing = dialog.ConfirmBeforeClosingSelected;
                _autoSaveIntervalMinutes = dialog.AutoSaveIntervalMinutesSelected;
                _uiScale = dialog.UiScaleSelected;
                _editorTabSize = dialog.TabSizeSelected;
                _editorFontFamily = dialog.FontFamilySelected;

                StatusText.Visibility = _showStatusBar ? Visibility.Visible : Visibility.Collapsed;

                Properties.Settings.Default.EnableWordWrap = _enableWordWrap;
                Properties.Settings.Default.EnableAutoSave = _enableAutoSave;
                Properties.Settings.Default.ShowStatusBar = _showStatusBar;
                Properties.Settings.Default.ConfirmBeforeClosing = _confirmBeforeClosing;
                Properties.Settings.Default.AutoSaveIntervalMinutes = _autoSaveIntervalMinutes;
                Properties.Settings.Default.UiScale = _uiScale;
                Properties.Settings.Default.EditorTabSize = _editorTabSize;
                Properties.Settings.Default.EditorFontFamily = _editorFontFamily;
                Properties.Settings.Default.Save();
            }

            dialog.Resources = Resources;
            dialog.PreferencesChanged += ApplyPreferences;
            dialog.Closed += (_, __) => dialog.PreferencesChanged -= ApplyPreferences;
            dialog.ShowDialog();
        }

        private void GitChangesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ActivateCommitOverlay();
            UpdateLeftTabState();
        }

        private void TopMenuItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element?.ContextMenu == null) return;

            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void EditorEditCommand_Click(object sender, RoutedEventArgs e)
        {
            TypifyDocumentView activeView = GetActiveDocumentView();
            if (activeView == null) return;

            var menuItem = sender as MenuItem;
            string command = menuItem?.Tag as string;

            switch (command)
            {
                case "Undo":
                    activeView.Undo();
                    break;
                case "Redo":
                    activeView.Redo();
                    break;
                case "Cut":
                    activeView.Cut();
                    break;
                case "Copy":
                    activeView.Copy();
                    break;
                case "Paste":
                    activeView.Paste();
                    break;
                case "SelectAll":
                    activeView.SelectAll();
                    break;
            }
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            NativeWindowStyle.Minimize(this);
        }

        private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            NativeWindowStyle.MaximizeOrRestore(this);
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowStateControls();
            NativeWindowStyle.UpdateWindowCornerPreference(this);
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            NativeWindowStyle.ApplyWindowFrame(this);
        }

        private void UpdateWindowStateControls()
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            mainGrid.Margin = isMaximized ? new Thickness(5) : new Thickness(0);
            MaximizeWindowGlyph.Text = isMaximized ? "\uE923" : "\uE922";
            MaximizeWindowButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        }

        private void ApplyTheme(bool isDark)
        {
            SetThemeBrush("WindowBackgroundBrush", isDark ? "#1E1E1E" : "#FFFFFF");
            SetThemeBrush("SurfaceBrush", isDark ? "#252526" : "#F3F3F3");
            SetThemeBrush("SidebarBrush", isDark ? "#252526" : "#F3F3F3");
            SetThemeBrush("BorderBrush", isDark ? "#2B2B2B" : "#E5E5E5");
            SetThemeBrush("TextBrush", isDark ? "#CCCCCC" : "#333333");
            SetThemeBrush("MutedTextBrush", isDark ? "#858585" : "#616161");
            SetThemeBrush("AccentBrush", isDark ? "#0E639C" : "#007ACC");
            SetThemeBrush("AccentHoverBrush", isDark ? "#1177BB" : "#0062A3");
            SetThemeBrush("AccentTextBrush", "#FFFFFF");
            SetThemeBrush("HoverBrush", isDark ? "#2A2D2E" : "#E8E8E8");
            SetThemeBrush("SelectedBrush", isDark ? "#094771" : "#E4E6F1");
            SetThemeBrush("EditorBackgroundBrush", isDark ? "#1E1E1E" : "#FFFFFF");
            SetThemeBrush("EditorTextBrush", isDark ? "#CCCCCC" : "#333333");
            SetThemeBrush("DiagnosticBackgroundBrush", isDark ? "#2B1B1D" : "#FFF2F1");
            SetThemeBrush("DiagnosticTextBrush", isDark ? "#F14C4C" : "#CF222E");
            SetThemeBrush("PreviewPageBorderBrush", isDark ? "#2B2B2B" : "#E5E5E5");
            SetThemeBrush("WindowCaptionButtonHoverBrush", isDark ? "#333333" : "#E0E0E0");

            foreach (TabItem tab in MainTabs.Items.OfType<TabItem>())
            {
                var document = tab.Tag as DocumentTabState;
                if (document != null)
                    document.View.SetDarkTheme(isDark);
            }

            GitTabView gitTabView = GetOpenGitTabView();
            if (gitTabView != null)
                gitTabView.UpdateTheme(isDark);

            CommitOverlay.UpdateTheme(isDark);
            ExplorerOverlay.UpdateTheme(isDark);
            ExplorerOverlay.SetWorkingDirectory(_workingDirectory);
        }

        private void SetThemeBrush(string key, string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _isClosing = true;
            foreach (TabItem tab in MainTabs.Items.OfType<TabItem>())
            {
                var state = tab.Tag as DocumentTabState;
                if (state != null)
                    state.View.Shutdown();
            }
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            DocumentTabState activeDocument = GetActiveDocumentState();
            if (activeDocument == null) return;

            string tempFile = Path.Combine(Path.GetTempPath(), "temp-" + Guid.NewGuid().ToString("N") + ".typ");
            File.WriteAllText(tempFile, activeDocument.View.DocumentText);

            string sourceDirectory = activeDocument.FilePath != null
                ? Path.GetDirectoryName(activeDocument.FilePath) ?? _workingDirectory
                : _workingDirectory;

            var saveDialog = new SaveFileDialog
            {
                Title = "Choose the output PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = _workingDirectory == "" ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _workingDirectory,
                FileName = _workingDirectory == "" ? ".pdf" : ""
            };
            if (saveDialog.ShowDialog(this) != true) return;

            StatusText.Text = "Creating PDF…";
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "typst",
                    Arguments = "compile --root " + QuoteArgument(sourceDirectory) + " --format pdf " + QuoteArgument(tempFile) + " " + QuoteArgument(saveDialog.FileName),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = sourceDirectory
                };

                string standardOutput;
                string standardError;
                int exitCode;
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start()) throw new InvalidOperationException("Could not start the Typst renderer.");
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    standardOutput = await outputTask;
                    standardError = await errorTask;
                    exitCode = process.ExitCode;
                }

                if (exitCode != 0)
                {
                    string details = string.Join(Environment.NewLine,
                        new[] { standardError, standardOutput }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    StatusText.Text = "PDF export failed";
                    MessageBox.Show(this,
                        string.IsNullOrWhiteSpace(details) ? "The Typst renderer could not complete the conversion." : details,
                        "Typst error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusText.Text = "PDF created: " + saveDialog.FileName;
            }
            catch (Exception exception)
            {
                StatusText.Text = "PDF export failed";
                string message = exception is Win32Exception
                    ? "The Typst compiler was not found. Install Typst and make sure typst.exe is available on PATH."
                    : exception.Message;
                MessageBox.Show(this, message, "Typst error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return argument;

            var result = new StringBuilder();
            result.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { backslashes++; continue; }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0) { result.Append('\\', backslashes); backslashes = 0; }
                result.Append(character);
            }
            if (backslashes > 0) result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private OpenFileDialog CreateOpenFileDialog()
        {
            return new OpenFileDialog
            {
                Title = "Open Typst file",
                Filter = "Typst files (*.typ)|*.typ|All files (*.*)|*.*",
                DefaultExt = ".typ",
                Multiselect = false,
                InitialDirectory = _workingDirectory == "" ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _workingDirectory
            };
        }

        private SaveFileDialog CreateSaveFileDialog()
        {
            return new SaveFileDialog
            {
                Title = "Save Typst file",
                Filter = "Typst files (*.typ)|*.typ|All files (*.*)|*.*",
                DefaultExt = ".typ",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = _workingDirectory == "" ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _workingDirectory
            };
        }

        private void OpenTypstFileWithDialog()
        {
            var openDialog = CreateOpenFileDialog();
            if (openDialog.ShowDialog(this) == true)
                OpenTypstFile(openDialog.FileName);
        }

        private void SaveActiveDocument()
        {
            DocumentTabState activeDocument = GetActiveDocumentState();
            if (activeDocument == null) return;

            if (!string.IsNullOrWhiteSpace(activeDocument.FilePath))
            {
                SaveDocumentToPath(activeDocument, activeDocument.FilePath);
                return;
            }

            var saveDialog = CreateSaveFileDialog();
            if (saveDialog.ShowDialog(this) == true)
                SaveDocumentToPath(activeDocument, saveDialog.FileName);
        }

        private void SaveActiveDocumentAs()
        {
            DocumentTabState activeDocument = GetActiveDocumentState();
            if (activeDocument == null) return;

            var saveDialog = CreateSaveFileDialog();
            if (saveDialog.ShowDialog(this) == true)
                SaveDocumentToPath(activeDocument, saveDialog.FileName);
        }

        private void CloseActiveDocumentTab()
        {
            var tab = MainTabs.SelectedItem as TabItem;
            if (tab == null) return;
            if (string.Equals(tab.Tag as string, GitTabMarker, StringComparison.Ordinal)) return;
            CloseDocumentTab(tab);
        }

        private void SelectAdjacentTab(int offset)
        {
            int count = MainTabs.Items.Count;
            if (count == 0 || offset == 0) return;

            int currentIndex = MainTabs.SelectedIndex;
            if (currentIndex < 0) currentIndex = 0;

            int targetIndex = (currentIndex + offset + count) % count;
            MainTabs.SelectedIndex = targetIndex;
        }

        private void ToggleLivePreview()
        {
            _isLivePreviewEnabled = !_isLivePreviewEnabled;
            ApplyLivePreviewToAllDocuments();
        }

        private async void FileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            switch (((MenuItem)sender).Tag as string)
            {
                case "New":
                    CreateAndSelectDocumentTab(null, string.Empty);
                    StatusText.Text = "New document created.";
                    break;
                case "Open":
                    OpenTypstFileWithDialog();
                    break;
                case "Save":
                    SaveActiveDocument();
                    break;
                case "SaveAs":
                    SaveActiveDocumentAs();
                    break;
                case "Print":
                    await PrintDocumentAsync();
                    break;
                case "Preferences":
                    ShowPreferencesWindow();
                    break;
            }
        }

        private void ExplorerOverlay_FileOpenRequested(object sender, ExplorerOverlay.FileOpenRequestedEventArgs e)
        {
            OpenTypstFile(e.FilePath);
        }

        private void OpenTypstFile(string filePath)
        {
            try
            {
                CreateAndSelectDocumentTab(filePath, null);
                StatusText.Text = "Opened: " + filePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error opening file", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to open file.";
            }
        }

        private async Task PrintDocumentAsync()
        {
            DocumentTabState activeDocument = GetActiveDocumentState();
            if (activeDocument == null) return;

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            string sourceDirectory = activeDocument.FilePath != null
                ? Path.GetDirectoryName(activeDocument.FilePath) ?? _workingDirectory
                : _workingDirectory;

            StatusText.Text = "Preparing document for printing…";
            try
            {
                TypstPreviewResult result = await LiveTypstPreview.CompileAsync(
                    activeDocument.View.DocumentText, _workingDirectory, sourceDirectory, CancellationToken.None, 300);

                if (result.ExitCode != 0 || result.Pages == null || result.Pages.Count == 0)
                {
                    string details = string.IsNullOrWhiteSpace(result.Diagnostics)
                        ? "Typst did not produce any pages to print."
                        : result.Diagnostics;
                    StatusText.Text = "Printing failed";
                    MessageBox.Show(this, details, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                double pageWidth = printDialog.PrintableAreaWidth;
                double pageHeight = printDialog.PrintableAreaHeight;
                var document = new FixedDocument();
                document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

                foreach (BitmapSource page in result.Pages)
                {
                    var fixedPage = new FixedPage { Width = pageWidth, Height = pageHeight };
                    var image = new Image
                    {
                        Source = page,
                        Stretch = Stretch.Uniform,
                        Width = pageWidth,
                        Height = pageHeight
                    };
                    FixedPage.SetLeft(image, 0);
                    FixedPage.SetTop(image, 0);
                    fixedPage.Children.Add(image);

                    var pageContent = new PageContent();
                    ((IAddChild)pageContent).AddChild(fixedPage);
                    document.Pages.Add(pageContent);
                }

                printDialog.PrintDocument(document.DocumentPaginator, "Typify document");
                StatusText.Text = "Document sent to printer.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Printing failed";
                MessageBox.Show(this, exception.Message, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
