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
        private const int DwmWindowCornerPreferenceAttribute = 33;
        private const int DwmTransitionsForcedDisabledAttribute = 3;
        private const int DwmWindowCornerDoNotRound = 1;
        private const int DwmWindowCornerRound = 2;
        private const int ShowWindowMinimize = 6;
        private const int ShowWindowMaximize = 3;
        private const int ShowWindowRestore = 9;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        private const int PreviewDebounceMilliseconds = 50;
        private readonly DispatcherTimer _previewTimer;
        private CancellationTokenSource _previewCancellation;
        private string _lastRequestedSource;
        private string _workingDirectory = string.Empty;
        private string _currentSourceDirectory = string.Empty;
        private int _previewVersion;
        private bool _isClosing;
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
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;
            InitializeComponent();
            UpdateWindowStateControls();
            Editor.TextChanged += Editor_TextChanged;
            KeyDown += MainWindow_KeyDown;
            ExplorerOverlay.FileOpenRequested += ExplorerOverlay_FileOpenRequested;
            ExplorerOverlay.DockToggleRequested += ExplorerOverlay_DockToggleRequested;
            ExplorerOverlay.CloseDockedRequested += ExplorerOverlay_CloseDockedRequested;
            CommitOverlay.DockToggleRequested += CommitOverlay_DockToggleRequested;
            CommitOverlay.CloseDockedRequested += CommitOverlay_CloseDockedRequested;
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
            await GitView.InitializeAsync(_workingDirectory, _isDarkTheme);
            await CommitOverlay.InitializeAsync(_workingDirectory, _isDarkTheme);
            CommitOverlay.CommitCompleted += CommitOverlay_CommitCompleted;
            CommitOverlay.VisibilityChanged += (s, ev) => UpdateLeftTabState();
            CommitOverlay.StatusUpdated += (s, ev) => UpdateLeftTabState();
            GitView.RepositoryRefreshed += GitView_RepositoryRefreshed;
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
            await GitView.RefreshAllAsync();
            StatusText.Text = "Commit completed successfully.";
            UpdateLeftTabState();
        }

        private async void GitView_RepositoryRefreshed(object sender, EventArgs e)
        {
            await CommitOverlay.RefreshStatusAsync();
            UpdateLeftTabState();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
                e.Key == Key.C)
            {
                e.Handled = true;
                ActivateCommitOverlay();
                UpdateLeftTabState();
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void LivePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            _isLivePreviewEnabled = !_isLivePreviewEnabled;
            PreviewPanel.Visibility = _isLivePreviewEnabled ? Visibility.Visible : Visibility.Collapsed;
            PreviewSplitter.Visibility = _isLivePreviewEnabled ? Visibility.Visible : Visibility.Collapsed;
            PreviewSplitterColumn.Width = _isLivePreviewEnabled ? new GridLength(6) : new GridLength(0);
            PreviewColumn.Width = _isLivePreviewEnabled
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);

            if (_isLivePreviewEnabled)
            {
                QueuePreviewCompile();
                return;
            }

            _previewTimer.Stop();
            _previewVersion++;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            PageHost.Children.Clear();
            PreviewDiagnostics.Clear();
            PreviewDiagnostics.Visibility = Visibility.Collapsed;
            PreviewStatus.Text = "Live preview is off";
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
            var menuItem = sender as MenuItem;
            string command = menuItem?.Tag as string;

            switch (command)
            {
                case "Undo":
                    if (Editor.CanUndo) Editor.Undo();
                    break;
                case "Redo":
                    if (Editor.CanRedo) Editor.Redo();
                    break;
                case "Cut":
                    Editor.Cut();
                    break;
                case "Copy":
                    Editor.Copy();
                    break;
                case "Paste":
                    Editor.Paste();
                    break;
                case "SelectAll":
                    Editor.SelectAll();
                    break;
            }
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWindow(new WindowInteropHelper(this).Handle, ShowWindowMinimize);
        }

        private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            int command = WindowState == WindowState.Maximized ? ShowWindowRestore : ShowWindowMaximize;
            ShowWindow(new WindowInteropHelper(this).Handle, command);
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowStateControls();
            UpdateWindowCornerPreference();
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            ApplyOverlappedWindowStyle();
            UpdateWindowCornerPreference();
        }

        private void UpdateWindowStateControls()
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            mainGrid.Margin = isMaximized ? new Thickness(5) : new Thickness(0);
            MaximizeWindowGlyph.Text = isMaximized ? "\uE923" : "\uE922";
            MaximizeWindowButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        }

        private void UpdateWindowCornerPreference()
        {
            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero) return;

            int preference = WindowState == WindowState.Maximized
                ? DwmWindowCornerDoNotRound
                : DwmWindowCornerRound;

            try
            {
                int transitionsForcedDisabled = 0;
                DwmSetWindowAttribute(
                    windowHandle,
                    DwmTransitionsForcedDisabledAttribute,
                    ref transitionsForcedDisabled,
                    Marshal.SizeOf(typeof(int)));

                DwmSetWindowAttribute(
                    windowHandle,
                    DwmWindowCornerPreferenceAttribute,
                    ref preference,
                    Marshal.SizeOf(typeof(int)));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
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

            Editor.IsDarkTheme = isDark;
            GitView.UpdateTheme(isDark);
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
            _previewTimer.Stop();
            if (_previewCancellation != null) _previewCancellation.Cancel();
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLivePreviewEnabled) return;
            string source = Editor.Text ?? string.Empty;
            if (string.Equals(source, _lastRequestedSource, StringComparison.Ordinal)) return;
            QueuePreviewCompile();
        }

        private void QueuePreviewCompile()
        {
            if (_isClosing || !_isLivePreviewEnabled) return;
            _lastRequestedSource = Editor.Text ?? string.Empty;
            _previewVersion++;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _previewTimer.Stop();
            _previewTimer.Start();
            PreviewStatus.Text = "Changes detected · updating preview…";
        }

        private async void PreviewTimer_Tick(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            if (_isClosing || !_isLivePreviewEnabled) return;
            int version = _previewVersion;
            var cancellation = new CancellationTokenSource();
            _previewCancellation = cancellation;
            try
            {
                TypstPreviewResult result = await LiveTypstPreview.CompileAsync(
                    _lastRequestedSource, _workingDirectory, _currentSourceDirectory, cancellation.Token);
                if (_isClosing || version != _previewVersion || result.WasCancelled) return;

                if (result.ExitCode == 0 && result.Pages != null && result.Pages.Count > 0)
                {
                    ShowPreviewPages(result.Pages);
                    PreviewDiagnostics.Clear();
                    PreviewDiagnostics.Visibility = Visibility.Collapsed;
                    PreviewStatus.Text = result.Pages.Count == 1
                        ? "Up to date · 1 page"
                        : "Up to date · " + result.Pages.Count + " pages";
                }
                else
                {
                    PreviewDiagnostics.Text = string.IsNullOrWhiteSpace(result.Diagnostics)
                        ? "Typst did not produce any preview pages."
                        : result.Diagnostics;
                    PreviewDiagnostics.Visibility = Visibility.Visible;
                    PreviewStatus.Text = PageHost.Children.Count == 0
                        ? "Compilation failed"
                        : "Compilation failed · showing the last successful preview";
                }
            }
            catch (Exception exception)
            {
                if (_isClosing || version != _previewVersion) return;
                PreviewDiagnostics.Text = exception.Message;
                PreviewDiagnostics.Visibility = Visibility.Visible;
                PreviewStatus.Text = "Could not update preview";
            }
            finally
            {
                if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
                cancellation.Dispose();
            }
        }

        private void ShowPreviewPages(System.Collections.Generic.IList<BitmapSource> pages)
        {
            PageHost.Children.Clear();
            for (int page = 0; page < pages.Count; page++)
            {
                var image = new Image
                {
                    Source = pages[page],
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = GetPreviewPageWidth()
                };
                var sheet = new Border
                {
                    Child = image,
                    Background = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Margin = new Thickness(8, 4, 8, 12)
                };
                sheet.SetResourceReference(Border.BorderBrushProperty, "PreviewPageBorderBrush");
                PageHost.Children.Add(sheet);
            }
            PreviewScroll.ScrollToTop();
        }

        private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double pageWidth = GetPreviewPageWidth();
            foreach (Border sheet in PageHost.Children)
            {
                var image = sheet.Child as Image;
                if (image != null) image.Width = pageWidth;
            }
        }

        private double GetPreviewPageWidth()
        {
            double available = PreviewScroll.ViewportWidth;
            if (double.IsNaN(available) || double.IsInfinity(available) || available <= 0)
                available = PreviewScroll.ActualWidth;
            return Math.Max(180, available - 36);
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "temp.typ");
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            File.WriteAllText(tempFile, Editor.Text);
            FileInfo fileInfo = new FileInfo(tempFile)
            {
                Attributes = FileAttributes.Hidden
            };

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
                    Arguments = "compile --format pdf " + QuoteArgument(tempFile) + " " + QuoteArgument(saveDialog.FileName),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(tempFile)
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

        private async void FileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            switch (((MenuItem)sender).Tag as string)
            {
                case "New":
                    Editor.Text = string.Empty;
                    StatusText.Text = "New document created.";
                    break;
                case "Open":
                    var openDialog = new OpenFileDialog
                    {
                        Title = "Open Typst file",
                        Filter = "Typst files (*.typ)|*.typ|All files (*.*)|*.*",
                        DefaultExt = ".typ",
                        Multiselect = false,
                        InitialDirectory = _workingDirectory == "" ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _workingDirectory
                    };
                    if (openDialog.ShowDialog(this) == true)
                    {
                        OpenTypstFile(openDialog.FileName);
                    }
                    break;
                case "Save":
                case "SaveAs":
                    var saveDialog = new SaveFileDialog
                    {
                        Title = "Save Typst file",
                        Filter = "Typst files (*.typ)|*.typ|All files (*.*)|*.*",
                        DefaultExt = ".typ",
                        AddExtension = true,
                        OverwritePrompt = true,
                        InitialDirectory = _workingDirectory == "" ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _workingDirectory
                    };
                    if (saveDialog.ShowDialog(this) == true)
                    {
                        try
                        {
                            File.WriteAllText(saveDialog.FileName, Editor.Text);
                            StatusText.Text = "Saved: " + saveDialog.FileName;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, ex.Message, "Error saving file", MessageBoxButton.OK, MessageBoxImage.Error);
                            StatusText.Text = "Failed to save file.";
                        }
                    }

                    break;
                case "Print":
                    await PrintDocumentAsync();
                    break;
                case "Preferences":
                    // Handle preferences
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
                Editor.Text = File.ReadAllText(filePath);
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
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            StatusText.Text = "Preparing document for printing…";
            try
            {
                TypstPreviewResult result = await LiveTypstPreview.CompileAsync(
                    Editor.Text, _workingDirectory, _currentSourceDirectory, CancellationToken.None, 300);

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
