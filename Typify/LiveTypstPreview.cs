using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Typify
{
    internal sealed class TypstPreviewResult
    {
        internal int ExitCode;
        internal bool WasCancelled;
        internal string Diagnostics;
        internal IList<BitmapSource> Pages;
    }

    internal static class LiveTypstPreview
    {
        private const int PreviewPpi = 72;

        internal static async Task<TypstPreviewResult> CompileAsync(
            string source, string sourceDirectory, CancellationToken cancellationToken)
        {
            string runDirectory = Path.Combine(Path.GetTempPath(), "Typify", "Preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            string inputPath = null;

            try
            {
                string inputDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                    ? runDirectory
                    : Path.GetFullPath(sourceDirectory);
                Directory.CreateDirectory(inputDirectory);
                inputPath = Path.Combine(inputDirectory, "TypifyPreview-" + Guid.NewGuid().ToString("N") + ".typ");
                string outputPattern = Path.Combine(runDirectory, "page-{p}-of-{t}.png");
                File.WriteAllText(inputPath, source ?? string.Empty, new UTF8Encoding(false));

                var startInfo = new ProcessStartInfo
                {
                    FileName = "typst",
                    Arguments = "compile --format png --ppi " + PreviewPpi + " --diagnostic-format short " +
                        QuoteArgument(inputPath) + " " + QuoteArgument(outputPattern),
                    WorkingDirectory = inputDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Der Typst-Compiler konnte nicht gestartet werden.");

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    using (cancellationToken.Register(() => TryKill(process)))
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);

                    string standardOutput = await outputTask.ConfigureAwait(false);
                    string standardError = await errorTask.ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                        return new TypstPreviewResult { WasCancelled = true };

                    string diagnostics = string.Join(Environment.NewLine,
                        new[] { standardError, standardOutput }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    var pages = new List<BitmapSource>();
                    if (process.ExitCode == 0)
                    {
                        foreach (string pagePath in Directory.GetFiles(runDirectory, "page-*-of-*.png").OrderBy(GetPageNumber))
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.UriSource = new Uri(pagePath, UriKind.Absolute);
                            image.EndInit();
                            image.Freeze();
                            pages.Add(image);
                        }
                    }

                    return new TypstPreviewResult
                    {
                        ExitCode = process.ExitCode,
                        Diagnostics = diagnostics,
                        Pages = pages
                    };
                }
            }
            catch (Win32Exception exception)
            {
                return new TypstPreviewResult
                {
                    ExitCode = 127,
                    Diagnostics = "Der Typst-Compiler wurde nicht gefunden. Installiere Typst und stelle sicher, dass typst.exe im PATH liegt." +
                        Environment.NewLine + exception.Message,
                    Pages = new List<BitmapSource>()
                };
            }
            finally
            {
                try { if (inputPath != null && File.Exists(inputPath)) File.Delete(inputPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                try { if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (SystemException) { }
        }

        private static int GetPageNumber(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int separator = name.LastIndexOf('-');
            int page;
            return separator >= 0 && int.TryParse(name.Substring(separator + 1), out page) ? page : int.MaxValue;
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
