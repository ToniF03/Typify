using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Typify
{
    /// <summary>
    /// Interaction logic for ExplorerOverlay.xaml
    /// </summary>
    public partial class ExplorerOverlay : UserControl
    {
        private bool _isDarkTheme;
        private string _workingDirectory;
        private readonly List<string> _rootDirectories = new List<string>();

        /// <summary>
        /// Gets the first root for compatibility with callers that expect a single working directory.
        /// Setting this property replaces the configured roots with the specified directory.
        /// </summary>
        public string WorkingDirectory
        {
            get { return _workingDirectory; }
            set
            {
                _rootDirectories.Clear();
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                {
                    _rootDirectories.Add(System.IO.Path.GetFullPath(value));
                }

                _workingDirectory = _rootDirectories.Count > 0 ? _rootDirectories[0] : value;
                UpdateRootView();
            }
        }

        /// <summary>
        /// Adds an existing directory as another top-level explorer root.
        /// Returns false when the path is invalid or already present.
        /// </summary>
        public bool AddRootDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

            string fullPath = System.IO.Path.GetFullPath(path);
            if (_rootDirectories.Any(root => string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase)))
                return false;

            _rootDirectories.Add(fullPath);
            if (string.IsNullOrWhiteSpace(_workingDirectory))
                _workingDirectory = fullPath;

            UpdateRootView();
            return true;
        }

        private void UpdateRootView()
        {
            bool hasRoots = _rootDirectories.Count > 0;
            if (hasRoots)
            {
                InitializeProjectTree();
                OpenFilePanel.Visibility = Visibility.Collapsed;
                ProjectTree.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = true;
                RefreshButton.IsEnabled = true;
                SearchButton.IsEnabled = true;
            }
            else
            {
                CancelPendingSearch();
                ProjectTree.Items.Clear();
                OpenFilePanel.Visibility = Visibility.Visible;
                ProjectTree.Visibility = Visibility.Collapsed;
                SearchTextBox.IsEnabled = false;
                RefreshButton.IsEnabled = false;
                SearchButton.IsEnabled = false;
                SearchTextBox.Text = string.Empty;
            }
        }

        private void AddRootDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NativeFolderDialog
            {
                Title = "Select a folder to add to the Explorer",
                InitialDirectory = Directory.Exists(_workingDirectory)
                    ? _workingDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog(Window.GetWindow(this)))
            {
                AddRootDirectory(dialog.SelectedPath);
            }
        }
        private Effect _floatingEffect;
        private const string DirectoryPlaceholderTag = "__TypifyDirectoryPlaceholder__";
        private static readonly Geometry FolderIconGeometry = Geometry.Parse("M1,3 H6 L8,5 H15 V13 H1 Z");
        private static readonly Geometry FileIconGeometry = Geometry.Parse("M3,1 H10 L14,5 V15 H3 Z");
        private static readonly Geometry FileIconDetailGeometry = Geometry.Parse("M10,1 V5 H14 M5,8 H11 M5,11 H11");
        private static readonly Geometry PDFFileIconGeometry = Geometry.Parse("M335.664 208.977 C344.89 189.701 356.236 179.941 369.414 179.941 C388.624 179.941 402.75 200.983 404.54 232.309 C406.76 271.377 394.542 306.259 380.974 338.701 C386.532 349.233 392.908 360.015 400 370.817 C416.06 395.265 437.556 417.349 462.44 434.989 C490.406 428.455 512.182 425.533 532.38 425.533 C551.248 425.533 567.85 428.165 583.132 433.583 C583.522 433.723 623.054 448.789 624.688 472.273 C625.008 476.703 624.248 487.789 610.454 494.973 C595.124 502.965 572.478 505.117 546.978 501.407 C516.704 497.013 485.662 485.039 456.796 466.681 C447.868 468.955 439.084 471.349 430.546 473.657 C422.478 475.865 414.612 477.993 407.044 479.917 L396.976 482.475 C368.864 489.571 339.86 496.913 312.644 506.749 C301.838 529.599 290.81 549.409 279.028 567.169 C273.174 576.005 242.314 620.059 210.25 620.059 C200.542 620.047 191.713 616.147 184.685 608.741 C178.07 601.765 174.815 593.059 175.309 583.565 C176.321 563.407 194.05 544.097 208.757 531.465 C231.206 512.149 258.143 496.415 290.932 483.441 C302.624 457.557 312.998 430.347 323.066 403.973 C331.158 382.763 339.518 360.853 348.516 339.719 C323.532 287.275 319.206 243.325 335.664 208.977 Z M369.014 210.839 C367.486 212.513 365.13 215.721 362.254 221.721 C351.04 245.129 355.22 274.123 364.19 300.379 C371.472 279.145 376.436 257.011 375.13 234.003 C374.478 222.635 371.374 214.483 369.014 210.839 Z M366.636 373.139 C361.136 386.897 355.818 400.835 350.61 414.479 C343.576 432.911 336.392 451.763 328.66 470.409 C349.006 464.207 369.606 458.999 389.754 453.901 L399.77 451.361 C407.198 449.489 414.906 447.385 422.828 445.229 C424.002 444.903 425.182 444.595 426.38 444.269 C406.524 427.441 389.172 408.003 375.368 386.979 C372.338 382.357 369.426 377.747 366.636 373.139 Z M501.468 457.411 C517.978 464.829 534.776 469.863 551.244 472.251 C566.588 474.477 580.9 474.153 590.648 471.275 C585.516 467.393 577.914 463.009 573.286 461.369 C553.75 454.447 530.91 453.127 501.468 457.411 Z M269.946 525.459 C254.147 533.875 240.264 543.265 227.982 553.827 C214.45 565.469 205.109 578.025 204.748 585.075 C204.69 586.303 204.882 587.203 206.079 588.459 C207.94 590.423 209.212 590.593 210.264 590.593 C220.955 590.593 241.765 570.061 254.489 550.885 C259.762 542.915 264.906 534.475 269.946 525.459 Z");
        private static readonly Brush FolderIconBrush = CreateIconBrush("#D6A354");
        private static readonly Brush TypstIconBrush = CreateIconBrush("#428CCB");
        private static readonly Brush PdfIconBrush = CreateIconBrush("#D75A5A");
        private static readonly Brush ImageIconBrush = CreateIconBrush("#A875CF");
        private static readonly Brush DataIconBrush = CreateIconBrush("#C79B39");
        private static readonly Brush CodeIconBrush = CreateIconBrush("#4E9A65");
        private static readonly Brush MarkupIconBrush = CreateIconBrush("#B46DB8");
        private static readonly Brush TextIconBrush = CreateIconBrush("#5B9A9A");
        private static readonly Brush DefaultFileIconBrush = CreateIconBrush("#8793A5");
        public bool IsOpen => Visibility == Visibility.Visible;
        private bool _isDocked = false;
        public bool IsDocked
        {
            get { return _isDocked; }
            set { _isDocked = value; TabHeaderBar.CornerRadius = value ? new CornerRadius(0) : new CornerRadius(0, 6, 6, 0); }
        }
        public event EventHandler VisibilityChanged;
        public event EventHandler DockToggleRequested;
        public event EventHandler CloseDockedRequested;
        public event EventHandler<FileOpenRequestedEventArgs> FileOpenRequested;

        public sealed class FileOpenRequestedEventArgs : EventArgs
        {
            public string FilePath { get; private set; }

            public FileOpenRequestedEventArgs(string filePath)
            {
                FilePath = filePath;
            }
        }

        public ExplorerOverlay()
        {
            InitializeComponent();
            _floatingEffect = OverlayWindowCard.Effect;
            this.Focusable = true;
            this.IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (!IsDocked && !this.IsKeyboardFocusWithin && IsOpen)
                {
                    ToggleVisibility();
                }
            };
        }

        public void SetWorkingDirectory(string path)
        {
            WorkingDirectory = path;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsDocked)
            {
                CloseDockedRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            ToggleVisibility();
        }

        private void DockButton_Click(object sender, RoutedEventArgs e)
        {
            DockToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetDocked(bool isDocked)
        {
            IsDocked = isDocked;
            HorizontalAlignment = isDocked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            OverlayWindowCard.HorizontalAlignment = isDocked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            OverlayWindowCard.Width = isDocked ? double.NaN : 360;
            OverlayWindowCard.CornerRadius = isDocked ? new CornerRadius(0) : new CornerRadius(0, 6, 6, 0);
            OverlayWindowCard.Effect = isDocked ? null : _floatingEffect;
            DockButton.ToolTip = isDocked ? "Undock from left edge" : "Dock to left edge";
            DockButtonIcon.Text = isDocked ? "↗" : "📌";
        }

        private void RefreshExplorer_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            InitializeProjectTree();
        }

        private static Brush CreateIconBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
                Visibility = Visibility.Collapsed;
            else
                ShowAndFocus();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ShowAndFocus()
        {
            Visibility = Visibility.Visible;
            OverlayWindowCard.Visibility = Visibility.Visible;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
            ProjectTree.Focus();
        }

        public void UpdateTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            ApplyTheme(isDark);
        }

        private void ApplyTheme(bool isDark)
        {
            SetThemeBrush("VsBackgroundBrush", isDark ? "#1E1E1E" : "#FFFFFF");
            SetThemeBrush("VsSidebarBrush", isDark ? "#252526" : "#F3F3F3");
            SetThemeBrush("VsHeaderBrush", isDark ? "#252526" : "#F3F3F3");
            SetThemeBrush("VsBorderBrush", isDark ? "#2B2B2B" : "#E5E5E5");
            SetThemeBrush("VsTextBrush", isDark ? "#CCCCCC" : "#333333");
            SetThemeBrush("VsMutedTextBrush", isDark ? "#858585" : "#616161");
            SetThemeBrush("VsPrimaryButtonBrush", isDark ? "#0E639C" : "#007ACC");
            SetThemeBrush("VsPrimaryButtonHoverBrush", isDark ? "#1177BB" : "#0062A3");
            SetThemeBrush("VsPrimaryButtonTextBrush", "#FFFFFF");
            SetThemeBrush("VsSecondaryButtonBrush", isDark ? "#3A3D41" : "#E1E1E1");
            SetThemeBrush("VsSecondaryButtonHoverBrush", isDark ? "#45494E" : "#D0D0D0");
            SetThemeBrush("VsIconButtonHoverBrush", isDark ? "#333333" : "#E0E0E0");
            SetThemeBrush("VsInputBackgroundBrush", isDark ? "#3C3C3C" : "#FFFFFF");
            SetThemeBrush("VsInputBorderBrush", isDark ? "#3C3C3C" : "#CECECE");
            SetThemeBrush("VsInputFocusBorderBrush", isDark ? "#007FD4" : "#007ACC");
            SetThemeBrush("VsPlaceholderBrush", isDark ? "#858585" : "#767676");
            SetThemeBrush("VsListHoverBrush", isDark ? "#2A2D2E" : "#E8E8E8");
            SetThemeBrush("VsListSelectedBrush", isDark ? "#094771" : "#E4E6F1");
            SetThemeBrush("VsTabActiveBorderBrush", isDark ? "#007FD4" : "#007ACC");
            SetThemeBrush("VsWarningBrush", isDark ? "#CCA700" : "#BF8803");
            SetThemeBrush("VsErrorBrush", isDark ? "#F14C4C" : "#E51400");
            SetThemeBrush("VsSuccessBrush", isDark ? "#89D185" : "#1A7F37");
            SetThemeBrush("VsBannerBackgroundBrush", isDark ? "#1C2D42" : "#DDEEFF");
            SetThemeBrush("VsBannerTextBrush", isDark ? "#9CDCFE" : "#005FB8");

            SetThemeBrush("VsStatusModifiedBrush", isDark ? "#E2C08D" : "#895503");
            SetThemeBrush("VsStatusUntrackedBrush", isDark ? "#73C991" : "#2EA043");
            SetThemeBrush("VsStatusAddedBrush", isDark ? "#73C991" : "#2EA043");
            SetThemeBrush("VsStatusDeletedBrush", isDark ? "#C74E39" : "#CF222E");
        }

        private void SetThemeBrush(string key, string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        private void InitializeProjectTree()
        {
            CancelPendingSearch();
            ProjectTree.Items.Clear();

            // Keep roots separate in the tree so each can be expanded and searched independently.
            foreach (string rootPath in _rootDirectories)
            {
                if (!Directory.Exists(rootPath)) continue;

                var root = CreateDirectoryNode(rootPath, GetRootDisplayName(rootPath));
                root.ToolTip = rootPath;
                ProjectTree.Items.Add(root);
                root.IsExpanded = true;
            }
        }

        private string GetRootDisplayName(string rootPath)
        {
            string rootName = System.IO.Path.GetFileName(rootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(rootName)) rootName = rootPath;

            int matchingNames = _rootDirectories.Count(path =>
            {
                string name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name)) name = path;
                return string.Equals(name, rootName, StringComparison.OrdinalIgnoreCase);
            });

            return matchingNames > 1 ? rootPath : rootName;
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

        private TreeViewItem CreateFileNode(string path, string fileName)
        {
            var item = new TreeViewItem
            {
                Header = CreateExplorerHeader(fileName, false),
                Tag = path,
                ToolTip = path
            };
            item.MouseDoubleClick += FileNode_MouseDoubleClick;
            return item;
        }

        private void FileNode_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var fileNode = sender as TreeViewItem;
            string filePath = fileNode?.Tag as string;
            if (string.IsNullOrWhiteSpace(filePath) ||
                !string.Equals(System.IO.Path.GetExtension(filePath), ".typ", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FileOpenRequested?.Invoke(this, new FileOpenRequestedEventArgs(filePath));
            e.Handled = true;
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
            else if (string.Equals(System.IO.Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdfIcon = new System.Windows.Shapes.Path
                {
                    Data = PDFFileIconGeometry,
                    Fill = PdfIconBrush,
                    Width = 16,
                    Height = 17,
                    Stretch = Stretch.Fill,
                    Margin = new Thickness(1, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                header.Children.Add(pdfIcon);
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
            string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
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

        private void DirectoryNode_Expanded(object sender, RoutedEventArgs e)
        {
            var directoryNode = sender as TreeViewItem;
            if (directoryNode == null || directoryNode.Items.Count != 1) return;

            var placeholder = directoryNode.Items[0] as TreeViewItem;
            if (placeholder == null || !string.Equals(placeholder.Tag as string, DirectoryPlaceholderTag, StringComparison.Ordinal))
                return;

            // The placeholder is replaced on first expansion, avoiding a full filesystem scan at startup.
            directoryNode.Items.Clear();
            string directoryPath = directoryNode.Tag as string;
            try
            {
                foreach (string subdirectory in Directory.GetDirectories(directoryPath).OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    directoryNode.Items.Add(CreateDirectoryNode(subdirectory, System.IO.Path.GetFileName(subdirectory)));

                foreach (string file in Directory.GetFiles(directoryPath).OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    if (fileName.StartsWith("TypifyPreview-", StringComparison.OrdinalIgnoreCase)) continue;
                    directoryNode.Items.Add(CreateFileNode(file, fileName));
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
    }
}
