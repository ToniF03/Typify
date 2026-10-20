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
        private string _workingDirectory = GetDefaultWorkingDirectory();
        private string _currentSourceDirectory = string.Empty;
        private int _previewVersion;
        private bool _isClosing;
        private bool _isDarkTheme;

        public MainWindow()
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;
            InitializeComponent();
            UpdateWindowStateControls();
            Editor.TextChanged += Editor_TextChanged;
            KeyDown += MainWindow_KeyDown;
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

        private static string GetDefaultWorkingDirectory()
        {
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string repositoryDirectory = Path.GetFullPath(Path.Combine(applicationDirectory, @"..\..\.."));
            return File.Exists(Path.Combine(repositoryDirectory, "Typify.slnx"))
                ? repositoryDirectory
                : applicationDirectory;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyTheme(false);
            QueuePreviewCompile();
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
            ThemeIcon.Text = isDark ? "☀" : "◐";
            ThemeLabel.Text = isDark ? "Light theme" : "Dark theme";
            ThemeToggleButton.ToolTip = isDark ? "Switch to light theme" : "Switch to dark theme";
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
            string source = Editor.Text ?? string.Empty;
            if (string.Equals(source, _lastRequestedSource, StringComparison.Ordinal)) return;
            QueuePreviewCompile();
        }

        private void QueuePreviewCompile()
        {
            if (_isClosing) return;
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
            var openDialog = new OpenFileDialog
            {
                Title = "Select a Typst source file",
                Filter = "Typst source files (*.typ)|*.typ|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (openDialog.ShowDialog(this) != true) return;

            var saveDialog = new SaveFileDialog
            {
                Title = "Choose the output PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Path.GetDirectoryName(openDialog.FileName),
                FileName = Path.GetFileNameWithoutExtension(openDialog.FileName) + ".pdf"
            };
            if (saveDialog.ShowDialog(this) != true) return;

            ExportButton.IsEnabled = false;
            StatusText.Text = "Creating PDF…";
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "typst",
                    Arguments = "compile --format pdf " + QuoteArgument(openDialog.FileName) + " " + QuoteArgument(saveDialog.FileName),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(openDialog.FileName)
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
                ExportButton.IsEnabled = true;
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
    }
}
