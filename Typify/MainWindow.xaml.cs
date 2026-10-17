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

namespace Typify
{
    public partial class MainWindow : Window
    {
        private const int PreviewDebounceMilliseconds = 50;
        private readonly DispatcherTimer _previewTimer;
        private CancellationTokenSource _previewCancellation;
        private string _lastRequestedSource;
        private string _previewSourceDirectory;
        private int _previewVersion;
        private bool _isClosing;

        public MainWindow()
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;
            InitializeComponent();
            Editor.TextChanged += Editor_TextChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            QueuePreviewCompile();
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
            PreviewStatus.Text = "Änderungen erkannt · Vorschau wird aktualisiert …";
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
                    _lastRequestedSource, _previewSourceDirectory, cancellation.Token);
                if (_isClosing || version != _previewVersion || result.WasCancelled) return;

                if (result.ExitCode == 0 && result.Pages != null && result.Pages.Count > 0)
                {
                    ShowPreviewPages(result.Pages);
                    PreviewDiagnostics.Clear();
                    PreviewDiagnostics.Visibility = Visibility.Collapsed;
                    PreviewStatus.Text = result.Pages.Count == 1
                        ? "Aktuell · 1 Seite"
                        : "Aktuell · " + result.Pages.Count + " Seiten";
                }
                else
                {
                    PreviewDiagnostics.Text = string.IsNullOrWhiteSpace(result.Diagnostics)
                        ? "Typst hat keine Vorschauseiten ausgegeben."
                        : result.Diagnostics;
                    PreviewDiagnostics.Visibility = Visibility.Visible;
                    PreviewStatus.Text = PageHost.Children.Count == 0
                        ? "Kompilierung fehlgeschlagen"
                        : "Kompilierungsfehler · letzte erfolgreiche Vorschau bleibt sichtbar";
                }
            }
            catch (Exception exception)
            {
                if (_isClosing || version != _previewVersion) return;
                PreviewDiagnostics.Text = exception.Message;
                PreviewDiagnostics.Visibility = Visibility.Visible;
                PreviewStatus.Text = "Vorschau konnte nicht aktualisiert werden";
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
                    BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 223)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Margin = new Thickness(8, 4, 8, 12)
                };
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

        private void ChooseImportDirectory_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Typst-Datei im Projektordner auswählen",
                Filter = "Typst-Dateien (*.typ)|*.typ|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (openDialog.ShowDialog(this) != true) return;

            _previewSourceDirectory = Path.GetDirectoryName(openDialog.FileName);
            ImportContextStatus.Text = "Importbasis: " + _previewSourceDirectory;
            ImportContextStatus.ToolTip = _previewSourceDirectory +
                Environment.NewLine + "Relative #import- und #include-Pfade werden aus diesem Ordner aufgelöst.";
            QueuePreviewCompile();
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Typst-Datei auswählen",
                Filter = "Typst-Dateien (*.typ)|*.typ|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (openDialog.ShowDialog(this) != true) return;

            var saveDialog = new SaveFileDialog
            {
                Title = "PDF-Ausgabedatei auswählen",
                Filter = "PDF-Dateien (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Path.GetDirectoryName(openDialog.FileName),
                FileName = Path.GetFileNameWithoutExtension(openDialog.FileName) + ".pdf"
            };
            if (saveDialog.ShowDialog(this) != true) return;

            ExportButton.IsEnabled = false;
            StatusText.Text = "PDF wird erstellt …";
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
                    if (!process.Start()) throw new InvalidOperationException("Der Renderer konnte nicht gestartet werden.");
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
                    StatusText.Text = "PDF-Erstellung fehlgeschlagen";
                    MessageBox.Show(this,
                        string.IsNullOrWhiteSpace(details) ? "Der Typst-Renderer hat die Umwandlung abgebrochen." : details,
                        "Typst-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusText.Text = "PDF erstellt: " + saveDialog.FileName;
            }
            catch (Exception exception)
            {
                StatusText.Text = "PDF-Erstellung fehlgeschlagen";
                string message = exception is Win32Exception
                    ? "Der Typst-Compiler wurde nicht gefunden. Installiere Typst und stelle sicher, dass typst.exe im PATH liegt."
                    : exception.Message;
                MessageBox.Show(this, message, "Typst-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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
