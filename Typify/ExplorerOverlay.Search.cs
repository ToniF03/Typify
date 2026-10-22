using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Typify
{
    public partial class ExplorerOverlay
    {
        private const int SearchDebounceMilliseconds = 200;
        private CancellationTokenSource _searchCancellation;

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rootDirectories.Count == 0) return;
            CancelPendingSearch();
            string searchText = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                InitializeProjectTree();
                return;
            }

            ProjectTree.Items.Clear();
            List<string> rootPaths = _rootDirectories.Where(Directory.Exists).ToList();
            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;

            try
            {
                if (!await DelayUnlessCancelledAsync(SearchDebounceMilliseconds, cancellation.Token)) return;

                // Scan independent roots in one background operation so typing never blocks the UI.
                List<SearchDirectoryResult> results = await Task.Run(
                    () => rootPaths.Select(rootPath => FindSearchMatches(rootPath, searchText, cancellation.Token)).ToList());

                if (cancellation.IsCancellationRequested || results == null) return;

                foreach (SearchDirectoryResult result in results)
                {
                    if (result == null) continue;

                    var root = CreateDirectoryNode(result.Path, GetRootDisplayName(result.Path));
                    root.ToolTip = result.Path;
                    root.Items.Clear();
                    AddSearchResults(root, result);
                    ProjectTree.Items.Add(root);
                    root.IsExpanded = true;
                }
            }
            finally
            {
                if (ReferenceEquals(_searchCancellation, cancellation))
                {
                    _searchCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private static async Task<bool> DelayUnlessCancelledAsync(int milliseconds, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return false;

            Task delayTask = Task.Delay(milliseconds);
            var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
            {
                await Task.WhenAny(delayTask, cancellationSignal.Task);
            }

            return !cancellationToken.IsCancellationRequested;
        }

        private void CancelPendingSearch()
        {
            if (_searchCancellation == null) return;

            _searchCancellation.Cancel();
            _searchCancellation = null;
        }

        private static SearchDirectoryResult FindSearchMatches(string rootPath, string searchText, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            if (!Directory.Exists(rootPath)) return null;
            return SearchDirectory(rootPath, searchText, cancellationToken);
        }

        private static SearchDirectoryResult SearchDirectory(string directoryPath, string searchText, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            var result = new SearchDirectoryResult
            {
                Path = directoryPath,
                Name = Path.GetFileName(directoryPath)
            };

            try
            {
                foreach (string subdirectory in Directory.EnumerateDirectories(directoryPath))
                {
                    if (cancellationToken.IsCancellationRequested) return null;
                    string directoryName = Path.GetFileName(subdirectory);
                    SearchDirectoryResult child = SearchDirectory(subdirectory, searchText, cancellationToken);
                    if (child == null) return null;
                    child.Name = directoryName;
                    child.DirectoryMatches = directoryName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (child.DirectoryMatches || child.HasChildMatches)
                    {
                        result.Directories.Add(child);
                        result.HasChildMatches = true;
                    }
                }

                foreach (string file in Directory.EnumerateFiles(directoryPath))
                {
                    if (cancellationToken.IsCancellationRequested) return null;
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("TypifyPreview-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (fileName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    result.Files.Add(new SearchFileResult
                    {
                        Name = fileName,
                        Path = file
                    });
                    result.HasChildMatches = true;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            result.Directories.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            result.Files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

            return result;
        }

        private void AddSearchResults(TreeViewItem parent, SearchDirectoryResult result)
        {
            foreach (SearchDirectoryResult directory in result.Directories)
            {
                var directoryNode = CreateDirectoryNode(directory.Path, directory.Name);
                directoryNode.Items.Clear();
                AddSearchResults(directoryNode, directory);
                directoryNode.IsExpanded = directory.HasChildMatches;
                parent.Items.Add(directoryNode);
            }

            foreach (SearchFileResult file in result.Files)
            {
                parent.Items.Add(CreateFileNode(file.Path, file.Name));
            }
        }

        private sealed class SearchDirectoryResult
        {
            public string Path;
            public string Name;
            public bool DirectoryMatches;
            public bool HasChildMatches;
            public readonly List<SearchDirectoryResult> Directories = new List<SearchDirectoryResult>();
            public readonly List<SearchFileResult> Files = new List<SearchFileResult>();
        }

        private sealed class SearchFileResult
        {
            public string Path;
            public string Name;
        }
    }
}
