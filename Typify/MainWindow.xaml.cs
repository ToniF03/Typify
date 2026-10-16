using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Typify
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
