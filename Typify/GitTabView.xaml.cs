using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Gitify.Engine;

namespace Typify
{
    public partial class GitTabView : UserControl
    {
        private GitRepositoryService _repository;
        private bool _isDarkTheme;
        private bool _isBusy;
        private bool _isRefreshing;
        private int _diffRequestId;
        private int _historyRequestId;
        private int _historyLimit = 300;
        private string _diffSource = string.Empty;
        private string _historyDiffSource = string.Empty;

        public GitTabView()
        {
            InitializeComponent();
            RepositoryPathBox.Text = Environment.CurrentDirectory;
            _repository = new GitRepositoryService(RepositoryPathBox.Text);
            ApplyTheme(false);
        }

        public async Task InitializeAsync(string repositoryPath, bool isDarkTheme)
        {
            _isDarkTheme = isDarkTheme;
            ApplyTheme(isDarkTheme);
            SetRepositoryPath(repositoryPath);
            await RefreshAllAsync();
        }

        public void UpdateTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            ApplyTheme(isDark);
            SetDiffContent(DiffViewer, _diffSource);
            SetDiffContent(HistoryDiffViewer, _historyDiffSource);
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
            SetThemeBrush("DiffBrush", isDark ? "#10151C" : "#FBFCFE");
            SetThemeBrush("DiffAddedForeground", isDark ? "#7EE787" : "#1A7F37");
            SetThemeBrush("DiffAddedBackground", isDark ? "#132B1E" : "#EAF6EE");
            SetThemeBrush("DiffDeletedForeground", isDark ? "#FFA198" : "#CF222E");
            SetThemeBrush("DiffDeletedBackground", isDark ? "#341A1C" : "#FFEBE9");
            SetThemeBrush("DiffHunkForeground", isDark ? "#79C0FF" : "#0550AE");
            SetThemeBrush("DiffHunkBackground", isDark ? "#102A40" : "#DDF4FF");
            SetThemeBrush("DiffHeaderForeground", isDark ? "#D2A8FF" : "#8250DF");
            SetThemeBrush("DiffHeaderBackground", isDark ? "#271A36" : "#FBEFFF");
        }

        private void SetThemeBrush(string key, string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        private void SetDiffContent(RichTextBox viewer, string text)
        {
            if (ReferenceEquals(viewer, DiffViewer)) _diffSource = text ?? string.Empty;
            else if (ReferenceEquals(viewer, HistoryDiffViewer)) _historyDiffSource = text ?? string.Empty;
            viewer.Document.Blocks.Clear();
            viewer.Document.PagePadding = new Thickness(0);
            viewer.Document.PageWidth = 2000;
            viewer.Document.FontFamily = new FontFamily("Consolas");
            viewer.Document.FontSize = viewer.FontSize;

            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string line in lines)
            {
                string foregroundKey = null;
                string backgroundKey = null;
                if (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal) ||
                    line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal) ||
                    line.StartsWith("new file mode ", StringComparison.Ordinal) || line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
                    line.StartsWith("old mode ", StringComparison.Ordinal) || line.StartsWith("new mode ", StringComparison.Ordinal) ||
                    line.StartsWith("rename from ", StringComparison.Ordinal) || line.StartsWith("rename to ", StringComparison.Ordinal))
                {
                    foregroundKey = "DiffHeaderForeground";
                    backgroundKey = "DiffHeaderBackground";
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    foregroundKey = "DiffHunkForeground";
                    backgroundKey = "DiffHunkBackground";
                }
                else if (line.StartsWith("+", StringComparison.Ordinal))
                {
                    foregroundKey = "DiffAddedForeground";
                    backgroundKey = "DiffAddedBackground";
                }
                else if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    foregroundKey = "DiffDeletedForeground";
                    backgroundKey = "DiffDeletedBackground";
                }

                var paragraph = new Paragraph(new Run(line)) { Margin = new Thickness(0) };
                if (foregroundKey != null)
                {
                    paragraph.Foreground = (Brush)FindResource(foregroundKey);
                    paragraph.Background = (Brush)FindResource(backgroundKey);
                }
                viewer.Document.Blocks.Add(paragraph);
            }
            viewer.ScrollToHome();
        }

        private void SetRepositoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string fullPath = Path.GetFullPath(path.Trim());
            RepositoryPathBox.Text = fullPath;
            _repository = new GitRepositoryService(fullPath);
        }

        private async void OpenRepository_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a Git repository or project folder";
                dialog.SelectedPath = Directory.Exists(RepositoryPathBox.Text) ? RepositoryPathBox.Text : Environment.CurrentDirectory;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                SetRepositoryPath(dialog.SelectedPath);
                await RefreshAllAsync();
            }
        }

        private async void InitializeRepository_Click(object sender, RoutedEventArgs e)
        {
            try { SetRepositoryPath(RepositoryPathBox.Text); }
            catch (Exception exception) { ShowError("Invalid folder", exception.Message); return; }
            if (!Directory.Exists(_repository.RepositoryPath))
            {
                ShowError("Folder not found", "Select an existing folder before initializing a repository.");
                return;
            }
            var result = await RunCommandAsync("Initialize repository", "init");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try { SetRepositoryPath(RepositoryPathBox.Text); }
            catch (Exception exception) { ShowError("Invalid folder", exception.Message); return; }
            await RefreshAllAsync();
        }

        private async Task RefreshAllAsync()
        {
            if (_isBusy || _isRefreshing) return;
            _isRefreshing = true;
            _diffRequestId++;
            _historyRequestId++;
            try
            {
                if (!Directory.Exists(_repository.RepositoryPath))
                {
                    SetNotRepository("Folder does not exist");
                    return;
                }

                var rootResult = await _repository.RunAsync("rev-parse", "--show-toplevel");
                if (!rootResult.Succeeded)
                {
                    SetNotRepository("Not a Git repository");
                    return;
                }

                string repositoryRoot = rootResult.StandardOutput.Trim();
                if (!string.IsNullOrWhiteSpace(repositoryRoot) && Directory.Exists(repositoryRoot))
                {
                    repositoryRoot = Path.GetFullPath(repositoryRoot);
                    if (!string.Equals(repositoryRoot, _repository.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _repository = new GitRepositoryService(repositoryRoot);
                        RepositoryPathBox.Text = repositoryRoot;
                    }
                }

                var statusTask = _repository.RunAsync("status", "--porcelain=v1", "-z", "--branch");
                var branchTask = _repository.RunAsync("branch", "--all", "--format=%(refname)%09%(HEAD)%09%(upstream:short)");
                var logTask = _repository.RunAsync("log", "--all", "--max-count=" + _historyLimit, "--date=iso-strict", "--format=%H%x00%h%x00%an%x00%ad%x00%s%x00");
                var remoteTask = _repository.RunAsync("remote", "-v");
                await Task.WhenAll(statusTask, branchTask, logTask, remoteTask);
                if (!statusTask.Result.Succeeded)
                {
                    SetNotRepository(statusTask.Result.CombinedOutput);
                    return;
                }

                string branchLine = (statusTask.Result.StandardOutput ?? string.Empty).Split('\n').FirstOrDefault() ?? string.Empty;
                BranchSummary.Text = branchLine.StartsWith("## ", StringComparison.Ordinal) ? branchLine.Substring(3).Trim() : "Repository";
                ChangesGrid.ItemsSource = GitRepositoryService.ParseStatus(statusTask.Result.StandardOutput);
                BranchesList.ItemsSource = ParseBranches(branchTask.Result.StandardOutput);
                HistoryList.ItemsSource = GitRepositoryService.ParseLog(logTask.Result.StandardOutput);
                RemotesText.Text = remoteTask.Result.Succeeded ? remoteTask.Result.StandardOutput.TrimEnd() : remoteTask.Result.CombinedOutput;
                RepositoryStatusText.Text = _repository.RepositoryPath;
                StatusText.Text = "Repository refreshed";
                SetDiffContent(DiffViewer, "Select a changed file to inspect its diff.");
                SetDiffContent(HistoryDiffViewer, string.Empty);
                CommitFilesList.ItemsSource = null;
            }
            catch (Exception exception)
            {
                SetNotRepository(exception.Message);
            }
            finally { _isRefreshing = false; }
        }

        private void SetNotRepository(string message)
        {
            BranchSummary.Text = "No repository";
            ChangesGrid.ItemsSource = null;
            BranchesList.ItemsSource = null;
            HistoryList.ItemsSource = null;
            CommitFilesList.ItemsSource = null;
            SetDiffContent(DiffViewer, "This folder is not a Git repository. Use Initialize, Open folder, or run `git clone` from the Git command tab.");
            SetDiffContent(HistoryDiffViewer, string.Empty);
            RemotesText.Clear();
            RepositoryStatusText.Text = message;
            StatusText.Text = message;
        }

        private static System.Collections.Generic.IList<GitBranch> ParseBranches(string output)
        {
            return (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    string[] columns = line.Split('\t');
                    if (columns.Length == 0) return null;
                    string refName = columns[0];
                    bool isRemote = refName.StartsWith("refs/remotes/", StringComparison.Ordinal);
                    string name = refName.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? refName.Substring("refs/heads/".Length)
                        : isRemote ? refName.Substring("refs/remotes/".Length) : refName;
                    if (isRemote && name.EndsWith("/HEAD", StringComparison.Ordinal)) return null;
                    return new GitBranch
                    {
                        Name = name,
                        IsCurrent = columns.Length > 1 && columns[1] == "*",
                        Upstream = columns.Length > 2 ? columns[2] : string.Empty,
                        IsRemote = isRemote
                    };
                })
                .Where(branch => branch != null)
                .ToList();
        }

        private async Task<GitCommandResult> RunCommandAsync(string description, params string[] arguments)
        {
            if (_isBusy) return null;
            _isBusy = true;
            RootLayout.IsEnabled = false;
            StatusText.Text = description + "…";
            try
            {
                var result = await _repository.RunAsync(arguments);
                string output = result.CombinedOutput;
                if (string.IsNullOrWhiteSpace(output)) output = result.Succeeded ? "Completed successfully." : "Git exited with code " + result.ExitCode + ".";
                CommandOutputText.Text = "$ git " + string.Join(" ", arguments.Select(GitRepositoryService.QuoteArgument)) + Environment.NewLine + output;
                StatusText.Text = result.Succeeded ? description + " complete" : description + " failed (exit " + result.ExitCode + ")";
                return result;
            }
            catch (Exception exception)
            {
                StatusText.Text = description + " failed";
                CommandOutputText.Text = exception.Message;
                return null;
            }
            finally
            {
                RootLayout.IsEnabled = true;
                _isBusy = false;
            }
        }

        private async void Fetch_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunCommandAsync("Fetch", "fetch", "--all", "--prune");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Pull_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunCommandAsync("Pull", "pull");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Push_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunCommandAsync("Push", "push");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Stage_Click(object sender, RoutedEventArgs e)
        {
            var change = ChangesGrid.SelectedItem as GitFileChange;
            if (change == null) { StatusText.Text = "Select a file to stage"; return; }
            var result = await RunCommandAsync("Stage file", "add", "--", change.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Unstage_Click(object sender, RoutedEventArgs e)
        {
            var change = ChangesGrid.SelectedItem as GitFileChange;
            if (change == null) { StatusText.Text = "Select a file to unstage"; return; }
            var result = await RunCommandAsync("Unstage file", "restore", "--staged", "--", change.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void Discard_Click(object sender, RoutedEventArgs e)
        {
            var change = ChangesGrid.SelectedItem as GitFileChange;
            if (change == null) { StatusText.Text = "Select a file to discard"; return; }
            if (change.WorkTreeStatus == ' ' && !change.IsUntracked)
            {
                StatusText.Text = "The selected file has no unstaged changes";
                return;
            }
            string action = change.IsUntracked ? "Delete the untracked file" : "Discard unstaged edits to";
            if (MessageBox.Show(Window.GetWindow(this), action + "\n\n" + change.Path + "\n\nThis cannot be undone.", "Discard changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            var result = change.IsUntracked
                ? await RunCommandAsync("Delete untracked file", "clean", "-fd", "--", change.Path)
                : await RunCommandAsync("Discard working changes", "restore", "--worktree", "--", change.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void ChangesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isRefreshing) return;
            var change = ChangesGrid.SelectedItem as GitFileChange;
            if (change == null || _isBusy) return;
            int requestId = ++_diffRequestId;
            DiffTitle.Text = change.Path;
            string path = change.Path;
            try
            {
                var diffs = new System.Collections.Generic.List<string>();
                if (change.IsUntracked)
                {
                    string absolutePath = Path.GetFullPath(Path.Combine(_repository.RepositoryPath, path));
                    var untracked = await _repository.RunAsync("diff", "--no-index", "--no-color", "--", "/dev/null", absolutePath);
                    if (!string.IsNullOrWhiteSpace(untracked.StandardOutput)) diffs.Add(untracked.StandardOutput);
                    else diffs.Add("Untracked file. Stage it to include it in the index.");
                }
                else
                {
                    if (change.IndexStatus != ' ')
                    {
                        var staged = await _repository.RunAsync("diff", "--cached", "--no-ext-diff", "--no-color", "--", path);
                        if (!string.IsNullOrWhiteSpace(staged.StandardOutput)) diffs.Add("Staged changes\n" + staged.StandardOutput);
                    }
                    if (change.WorkTreeStatus != ' ')
                    {
                        var unstaged = await _repository.RunAsync("diff", "--no-ext-diff", "--no-color", "--", path);
                        if (!string.IsNullOrWhiteSpace(unstaged.StandardOutput)) diffs.Add("Unstaged changes\n" + unstaged.StandardOutput);
                    }
                    if (diffs.Count == 0) diffs.Add("No text diff is available for this change.");
                }
                if (requestId == _diffRequestId && ReferenceEquals(ChangesGrid.SelectedItem, change))
                    SetDiffContent(DiffViewer, string.Join(Environment.NewLine + Environment.NewLine, diffs));
            }
            catch (Exception exception)
            {
                if (requestId == _diffRequestId) SetDiffContent(DiffViewer, exception.Message);
            }
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            string message = CommitMessageBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message)) { StatusText.Text = "Enter a commit message"; return; }
            var result = await RunCommandAsync("Commit", "commit", "-m", message);
            if (result != null && result.Succeeded)
            {
                CommitMessageBox.Clear();
                await RefreshAllAsync();
            }
        }

        private async void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isRefreshing) return;
            var commit = HistoryList.SelectedItem as GitCommit;
            if (commit == null || _isBusy) return;
            int requestId = ++_historyRequestId;
            try
            {
                var showTask = _repository.RunAsync("show", "--format=fuller", "--no-ext-diff", "--no-color", commit.Hash);
                var filesTask = _repository.RunAsync("diff-tree", "--root", "--no-commit-id", "--name-only", "-r", commit.Hash);
                await Task.WhenAll(showTask, filesTask);
                if (requestId == _historyRequestId && ReferenceEquals(HistoryList.SelectedItem, commit))
                {
                    SetDiffContent(HistoryDiffViewer, showTask.Result.CombinedOutput);
                    CommitFilesList.ItemsSource = (filesTask.Result.StandardOutput ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (Exception exception)
            {
                if (requestId == _historyRequestId) SetDiffContent(HistoryDiffViewer, exception.Message);
            }
        }

        private async void LoadOlderHistory_Click(object sender, RoutedEventArgs e)
        {
            _historyLimit += 300;
            await RefreshAllAsync();
        }

        private async void CheckoutCommit_Click(object sender, RoutedEventArgs e)
        {
            var commit = HistoryList.SelectedItem as GitCommit;
            if (commit == null) { StatusText.Text = "Select a commit"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "Check out " + commit.ShortHash + " in detached HEAD state?", "Check out commit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var result = await RunCommandAsync("Check out commit", "switch", "--detach", commit.Hash);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void RevertCommit_Click(object sender, RoutedEventArgs e)
        {
            var commit = HistoryList.SelectedItem as GitCommit;
            if (commit == null) { StatusText.Text = "Select a commit"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "Create a new commit that reverses " + commit.ShortHash + "?", "Revert commit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var result = await RunCommandAsync("Revert commit", "revert", "--no-edit", commit.Hash);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void RestoreHistoricalFile_Click(object sender, RoutedEventArgs e)
        {
            var commit = HistoryList.SelectedItem as GitCommit;
            string file = CommitFilesList.SelectedItem as string;
            if (commit == null || string.IsNullOrWhiteSpace(file)) { StatusText.Text = "Select a commit and one of its files"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "Restore this version into the working tree?\n\n" + file, "Restore file version", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await RunCommandAsync("Restore historical file", "restore", "--source=" + commit.Hash, "--worktree", "--", file);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private void CommitFilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

        private async void CreateBranch_Click(object sender, RoutedEventArgs e)
        {
            string name = NewBranchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { StatusText.Text = "Enter a branch name"; return; }
            var result = await RunCommandAsync("Create branch", "branch", name);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void SwitchBranch_Click(object sender, RoutedEventArgs e)
        {
            var branch = BranchesList.SelectedItem as GitBranch;
            if (branch == null) { StatusText.Text = "Select a branch"; return; }
            GitCommandResult result;
            if (branch.IsRemote)
            {
                int slash = branch.Name.IndexOf('/');
                if (slash <= 0 || slash == branch.Name.Length - 1) { StatusText.Text = "Select a remote branch with a valid name"; return; }
                string localName = branch.Name.Substring(slash + 1);
                result = await RunCommandAsync("Track remote branch", "switch", "--track", "-c", localName, branch.Name);
            }
            else result = await RunCommandAsync("Switch branch", "switch", branch.Name);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
        {
            var branch = BranchesList.SelectedItem as GitBranch;
            if (branch == null) { StatusText.Text = "Select a branch"; return; }
            if (branch.IsCurrent) { StatusText.Text = "Switch away from the current branch before deleting it"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "Delete branch " + branch.Name + "?", "Delete branch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            GitCommandResult result;
            if (branch.IsRemote)
            {
                int slash = branch.Name.IndexOf('/');
                if (slash <= 0 || slash == branch.Name.Length - 1) { StatusText.Text = "Invalid remote branch"; return; }
                result = await RunCommandAsync("Delete remote branch", "push", branch.Name.Substring(0, slash), "--delete", branch.Name.Substring(slash + 1));
            }
            else result = await RunCommandAsync("Delete branch", "branch", "-d", branch.Name);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void AddRemote_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RemoteNameBox.Text) || string.IsNullOrWhiteSpace(RemoteUrlBox.Text)) { StatusText.Text = "Enter a remote name and URL"; return; }
            var result = await RunCommandAsync("Add remote", "remote", "add", RemoteNameBox.Text.Trim(), RemoteUrlBox.Text.Trim());
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void SetRemoteUrl_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RemoteNameBox.Text) || string.IsNullOrWhiteSpace(RemoteUrlBox.Text)) { StatusText.Text = "Enter a remote name and URL"; return; }
            var result = await RunCommandAsync("Set remote URL", "remote", "set-url", RemoteNameBox.Text.Trim(), RemoteUrlBox.Text.Trim());
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void RemoveRemote_Click(object sender, RoutedEventArgs e)
        {
            string name = RemoteNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { StatusText.Text = "Enter a remote name"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "Remove remote " + name + "?", "Remove remote", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await RunCommandAsync("Remove remote", "remote", "remove", name);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private void BrowseCloneDestination_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the parent folder for the cloned repository";
                dialog.SelectedPath = Directory.Exists(RepositoryPathBox.Text) ? RepositoryPathBox.Text : Environment.CurrentDirectory;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) CloneDestinationBox.Text = dialog.SelectedPath;
            }
        }

        private async void Clone_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CloneUrlBox.Text) || string.IsNullOrWhiteSpace(CloneDestinationBox.Text)) { StatusText.Text = "Enter a repository URL and destination folder"; return; }
            string destination = CloneDestinationBox.Text.Trim();
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            {
                ShowError("Destination is not empty", "Choose a new folder or an empty destination folder.");
                return;
            }
            var result = await RunCommandAsync("Clone repository", "clone", CloneUrlBox.Text.Trim(), destination);
            if (result != null && result.Succeeded)
            {
                SetRepositoryPath(destination);
                await RefreshAllAsync();
            }
        }

        private async void RunGitCommand_Click(object sender, RoutedEventArgs e)
        {
            await RunGitCommandAsync();
        }

        private void RunInteractiveGitCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var arguments = GitRepositoryService.TokenizeCommandLine(GitCommandBox.Text);
                if (arguments.Count == 0) { StatusText.Text = "Enter a Git subcommand"; return; }
                _repository.StartInteractive(arguments);
                StatusText.Text = "Started git " + arguments[0] + " in a terminal window";
            }
            catch (Exception exception) { ShowError("Could not start Git", exception.Message); }
        }

        private async void GitCommandBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await RunGitCommandAsync();
            }
        }

        private async Task RunGitCommandAsync()
        {
            try
            {
                var arguments = GitRepositoryService.TokenizeCommandLine(GitCommandBox.Text);
                if (arguments.Count == 0) { StatusText.Text = "Enter a Git subcommand"; return; }
                var result = await RunCommandAsync("git " + arguments[0], arguments.ToArray());
                if (result != null && result.Succeeded) await RefreshAllAsync();
            }
            catch (FormatException exception) { ShowError("Invalid Git command", exception.Message); }
        }

        private void ShowError(string title, string message)
        {
            MessageBox.Show(Window.GetWindow(this), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
