using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Typify
{
    public partial class TypifyDocumentView : UserControl
    {
        private const int PreviewDebounceMilliseconds = 50;
        private readonly DispatcherTimer _previewTimer;
        private CancellationTokenSource _previewCancellation;
        private string _lastRequestedSource = string.Empty;
        private string _workingDirectory = string.Empty;
        private string _sourceDirectory = string.Empty;
        private int _previewVersion;
        private bool _isClosing;
        private bool _isLivePreviewEnabled;

        public TypifyDocumentView()
        {
            InitializeComponent();
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;
            Editor.TextChanged += Editor_TextChanged;
            Unloaded += TypifyDocumentView_Unloaded;
        }

        public string DocumentText
        {
            get { return Editor.Text ?? string.Empty; }
            set { Editor.Text = value ?? string.Empty; }
        }

        public string SourceDirectory { get { return _sourceDirectory; } }

        public void SetDarkTheme(bool isDark)
        {
            Editor.IsDarkTheme = isDark;
        }

        public void SetSourceContext(string workingDirectory, string sourceDirectory)
        {
            _workingDirectory = workingDirectory ?? string.Empty;
            _sourceDirectory = sourceDirectory ?? string.Empty;
            if (_isLivePreviewEnabled)
                QueuePreviewCompile();
        }

        public void SetLivePreviewEnabled(bool isEnabled)
        {
            _isLivePreviewEnabled = isEnabled;
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

        public void ToggleLivePreview()
        {
            SetLivePreviewEnabled(!_isLivePreviewEnabled);
        }

        public bool CanUndo { get { return Editor.CanUndo; } }
        public bool CanRedo { get { return Editor.CanRedo; } }

        public void Undo() { if (Editor.CanUndo) Editor.Undo(); }
        public void Redo() { if (Editor.CanRedo) Editor.Redo(); }
        public void Cut() { Editor.Cut(); }
        public void Copy() { Editor.Copy(); }
        public void Paste() { Editor.Paste(); }
        public void SelectAll() { Editor.SelectAll(); }
        public void FocusEditor() { Editor.Focus(); }

        public void Shutdown()
        {
            _isClosing = true;
            _previewTimer.Stop();
            if (_previewCancellation != null) _previewCancellation.Cancel();
        }

        private void TypifyDocumentView_Unloaded(object sender, RoutedEventArgs e)
        {
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
                    _lastRequestedSource, _workingDirectory, _sourceDirectory, cancellation.Token);
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

        private void ShowPreviewPages(IList<BitmapSource> pages)
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
    }
}
