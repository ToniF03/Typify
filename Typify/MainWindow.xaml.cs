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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Input;

namespace Typify
{
    public partial class MainWindow : Window
    {
        private const int PreviewDebounceMilliseconds = 50;
        private const string DirectoryPlaceholderTag = "__TypifyDirectoryPlaceholder__";
        private static readonly Geometry FolderIconGeometry = Geometry.Parse("M1,3 H6 L8,5 H15 V13 H1 Z");
        private static readonly Geometry FileIconGeometry = Geometry.Parse("M3,1 H10 L14,5 V15 H3 Z");
        private static readonly Geometry FileIconDetailGeometry = Geometry.Parse("M10,1 V5 H14 M5,8 H11 M5,11 H11");
        private static readonly Brush FolderIconBrush = CreateIconBrush("#D6A354");
        private static readonly Brush TypstIconBrush = CreateIconBrush("#428CCB");
        private static readonly Brush PdfIconBrush = CreateIconBrush("#D75A5A");
        private static readonly Brush ImageIconBrush = CreateIconBrush("#A875CF");
        private static readonly Brush DataIconBrush = CreateIconBrush("#C79B39");
        private static readonly Brush CodeIconBrush = CreateIconBrush("#4E9A65");
        private static readonly Brush MarkupIconBrush = CreateIconBrush("#B46DB8");
        private static readonly Brush TextIconBrush = CreateIconBrush("#5B9A9A");
        private static readonly Brush DefaultFileIconBrush = CreateIconBrush("#8793A5");
        private readonly DispatcherTimer _previewTimer;
        private CancellationTokenSource _previewCancellation;
        private string _lastRequestedSource;
        // Set this value to the root folder of the Typst project.
        private string _workingDirectory = GetDefaultWorkingDirectory();
        private string _currentSourceDirectory;
        private int _previewVersion;
        private bool _isClosing;
        private bool _isDarkTheme;

        public MainWindow()
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;
            InitializeComponent();
            Editor.TextChanged += Editor_TextChanged;
        }

        private static string GetDefaultWorkingDirectory()
        {
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string repositoryDirectory = Path.GetFullPath(Path.Combine(applicationDirectory, @"..\..\.."));
            return File.Exists(Path.Combine(repositoryDirectory, "Typify.slnx"))
                ? repositoryDirectory
                : applicationDirectory;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyTheme(false);
            InitializeProjectTree();
            QueuePreviewCompile();
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void ApplyTheme(bool isDark)
        {
            SetThemeBrush("WindowBackgroundBrush", isDark ? "#0E1117" : "#F4F6F9");
            SetThemeBrush("SurfaceBrush", isDark ? "#151A22" : "#FFFFFF");
            SetThemeBrush("SidebarBrush", isDark ? "#121720" : "#F8F9FB");
            SetThemeBrush("BorderBrush", isDark ? "#2A3340" : "#E1E5EB");
            SetThemeBrush("TextBrush", isDark ? "#E6EDF3" : "#202938");
            SetThemeBrush("MutedTextBrush", isDark ? "#8B98A9" : "#687386");
            SetThemeBrush("AccentBrush", isDark ? "#6EA8FE" : "#4F75D2");
            SetThemeBrush("AccentHoverBrush", isDark ? "#5794F4" : "#3E64BD");
            SetThemeBrush("AccentTextBrush", isDark ? "#101722" : "#FFFFFF");
            SetThemeBrush("HoverBrush", isDark ? "#202733" : "#F0F3F8");
            SetThemeBrush("SelectedBrush", isDark ? "#203149" : "#E8EEF9");
            SetThemeBrush("EditorBackgroundBrush", isDark ? "#10151C" : "#FBFCFE");
            SetThemeBrush("EditorTextBrush", isDark ? "#D8DEE9" : "#273142");
            SetThemeBrush("DiagnosticBackgroundBrush", isDark ? "#2B1B1D" : "#FFF2F1");
            SetThemeBrush("DiagnosticTextBrush", isDark ? "#FF938A" : "#9B3F38");
            SetThemeBrush("PreviewPageBorderBrush", isDark ? "#343E4B" : "#D7DCE4");

            Editor.IsDarkTheme = isDark;
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

        private void InitializeProjectTree()
        {
            ProjectTree.Items.Clear();
            if (!Directory.Exists(_workingDirectory))
            {
                ProjectTreeStatus.Text = "Working directory not found: " + _workingDirectory;
                return;
            }

            string rootPath = Path.GetFullPath(_workingDirectory);
            string rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var root = CreateDirectoryNode(rootPath, string.IsNullOrEmpty(rootName) ? rootPath : rootName);
            root.ToolTip = rootPath;
            ProjectTree.Items.Add(root);
            root.IsExpanded = true;
            ProjectTreeStatus.Text = "Double-click a .typ file to open it";
        }

        private void RefreshExplorer_Click(object sender, RoutedEventArgs e)
        {
            InitializeProjectTree();
        }

        private TreeViewItem CreateDirectoryNode(string path, string header)
        {
            var item = new TreeViewItem
            {
                Header = CreateExplorerHeader(header, true),
                Tag = path,
                ToolTip = path
            };
            item.Items.Add(new TreeViewItem { Header = "Loading…", Tag = DirectoryPlaceholderTag, IsEnabled = false });
            item.Expanded += DirectoryNode_Expanded;
            return item;
        }

        private object CreateExplorerHeader(string name, bool isDirectory)
        {
            var header = new Grid { VerticalAlignment = VerticalAlignment.Center };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (isDirectory)
            {
                var folderIcon = new System.Windows.Shapes.Path
                {
                    Data = FolderIconGeometry,
                    Fill = FolderIconBrush,
                    Width = 16,
                    Height = 14,
                    Stretch = Stretch.Fill,
                    Margin = new Thickness(1, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                header.Children.Add(folderIcon);
            }
            else
            {
                Brush iconBrush = GetFileIconBrush(name);
                var softBrush = (SolidColorBrush)iconBrush.Clone();
                softBrush.Opacity = 0.14;
                softBrush.Freeze();

                var icon = new Grid
                {
                    Width = 16,
                    Height = 17,
                    Margin = new Thickness(1, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = FileIconGeometry,
                    Fill = softBrush,
                    Stroke = iconBrush,
                    StrokeThickness = 1.1,
                    Stretch = Stretch.Fill
                });
                icon.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = FileIconDetailGeometry,
                    Stroke = iconBrush,
                    StrokeThickness = 1.1,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stretch = Stretch.Fill
                });
                header.Children.Add(icon);
            }

            var label = new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12.5
            };
            Grid.SetColumn(label, 1);
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            header.Children.Add(label);
            return header;
        }

        private static Brush GetFileIconBrush(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            switch (extension)
            {
                case ".typ": return TypstIconBrush;
                case ".pdf": return PdfIconBrush;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".svg": return ImageIconBrush;
                case ".json":
                case ".yaml":
                case ".yml": return DataIconBrush;
                case ".cs":
                case ".py":
                case ".js":
                case ".ts":
                case ".cpp":
                case ".h": return CodeIconBrush;
                case ".xaml":
                case ".xml": return MarkupIconBrush;
                case ".md":
                case ".txt": return TextIconBrush;
                default: return DefaultFileIconBrush;
            }
        }

        private static Brush CreateIconBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }

        private void DirectoryNode_Expanded(object sender, RoutedEventArgs e)
        {
            var directoryNode = sender as TreeViewItem;
            if (directoryNode == null || directoryNode.Items.Count != 1) return;

            var placeholder = directoryNode.Items[0] as TreeViewItem;
            if (placeholder == null || !string.Equals(placeholder.Tag as string, DirectoryPlaceholderTag, StringComparison.Ordinal))
                return;

            directoryNode.Items.Clear();
            string directoryPath = directoryNode.Tag as string;
            try
            {
                foreach (string subdirectory in Directory.GetDirectories(directoryPath).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    directoryNode.Items.Add(CreateDirectoryNode(subdirectory, Path.GetFileName(subdirectory)));

                foreach (string file in Directory.GetFiles(directoryPath).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("TypifyPreview-", StringComparison.OrdinalIgnoreCase)) continue;
                    directoryNode.Items.Add(new TreeViewItem { Header = CreateExplorerHeader(fileName, false), Tag = file, ToolTip = file });
                }
            }
            catch (UnauthorizedAccessException)
            {
                directoryNode.Items.Add(new TreeViewItem { Header = "Access denied", IsEnabled = false });
            }
            catch (IOException)
            {
                directoryNode.Items.Add(new TreeViewItem { Header = "Could not read folder", IsEnabled = false });
            }
        }

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedNode = FindTreeViewItem(e.OriginalSource as DependencyObject) ?? ProjectTree.SelectedItem as TreeViewItem;
            string filePath = selectedNode == null ? null : selectedNode.Tag as string;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !string.Equals(Path.GetExtension(filePath), ".typ", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string source = File.ReadAllText(filePath);
                _currentSourceDirectory = Path.GetDirectoryName(filePath);
                bool sourceIsUnchanged = string.Equals(Editor.Text ?? string.Empty, source, StringComparison.Ordinal);
                Editor.Text = source;
                SourceDocumentLabel.Text = Path.GetFileName(filePath);
                SourceDocumentLabel.ToolTip = filePath;
                StatusText.Text = "Opened: " + filePath;
                if (sourceIsUnchanged) QueuePreviewCompile();
            }
            catch (IOException exception)
            {
                StatusText.Text = "Could not open file";
                PreviewDiagnostics.Text = exception.Message;
                PreviewDiagnostics.Visibility = Visibility.Visible;
            }
            catch (UnauthorizedAccessException exception)
            {
                StatusText.Text = "Could not open file";
                PreviewDiagnostics.Text = exception.Message;
                PreviewDiagnostics.Visibility = Visibility.Visible;
            }
        }

        private static TreeViewItem FindTreeViewItem(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
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
