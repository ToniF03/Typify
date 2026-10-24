using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Gitify.Engine;

namespace Typify
{
    public class VsCodeChangeItem
    {
        public string Path { get; set; }
        public string OriginalPath { get; set; }
        public string FileName { get; set; }
        public string DirectoryDisplay { get; set; }
        public string StatusLetter { get; set; }
        public string StatusTooltip { get; set; }
        public bool IsStaged { get; set; }
        public bool IsUntracked { get; set; }
        public GitFileChange SourceChange { get; set; }
    }

    public partial class GitTabView : UserControl
    {
        private GitRepositoryService _repository;
        private bool _isDarkTheme;
        private bool _isBusy;
        private bool _isRefreshing;
        private bool _isSyncingSelection;
        private bool _isStagedExpanded = true;
        private bool _isUnstagedExpanded = true;
        private int _diffRequestId;
        private int _historyRequestId;
        private int _historyLimit = 300;
        private string _diffSource = string.Empty;
        private string _historyDiffSource = string.Empty;
        private VsCodeChangeItem _currentSelectedChange;
        private List<VsCodeChangeItem> _stagedItems = new List<VsCodeChangeItem>();
        private List<VsCodeChangeItem> _unstagedItems = new List<VsCodeChangeItem>();
        private IList<GitBranch> _branches = new List<GitBranch>();
        private string _currentBranchName = string.Empty;
        private int _aheadCount;
        private int _behindCount;

        public event EventHandler RepositoryRefreshed;

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
            // Typify standard brushes
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

            // VS Code Specific Palette
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
            SetThemeBrush("VsStatusBarBrush", isDark ? "#0E639C" : "#007ACC");
            SetThemeBrush("VsStatusBarTextBrush", "#FFFFFF");

            // Status letter badges
            SetThemeBrush("VsStatusModifiedBrush", isDark ? "#E2C08D" : "#895503");
            SetThemeBrush("VsStatusUntrackedBrush", isDark ? "#73C991" : "#2EA043");
            SetThemeBrush("VsStatusAddedBrush", isDark ? "#73C991" : "#2EA043");
            SetThemeBrush("VsStatusDeletedBrush", isDark ? "#C74E39" : "#CF222E");
            SetThemeBrush("VsStatusRenamedBrush", isDark ? "#4EC9B0" : "#0969DA");

            // Diff styling
            SetThemeBrush("DiffBrush", isDark ? "#1E1E1E" : "#FFFFFF");
            SetThemeBrush("VsDiffBackgroundBrush", isDark ? "#1E1E1E" : "#FFFFFF");
            SetThemeBrush("VsDiffHeaderBarBrush", isDark ? "#252526" : "#F3F3F3");
            SetThemeBrush("VsDiffAddedBg", isDark ? "#1B3B24" : "#E6FFEC");
            SetThemeBrush("VsDiffAddedFg", isDark ? "#89D185" : "#1A7F37");
            SetThemeBrush("VsDiffDeletedBg", isDark ? "#471C1E" : "#FFEBE9");
            SetThemeBrush("VsDiffDeletedFg", isDark ? "#F85149" : "#CF222E");
            SetThemeBrush("VsDiffHunkBg", isDark ? "#1C2A3A" : "#F1F8FF");
            SetThemeBrush("VsDiffHunkFg", isDark ? "#79C0FF" : "#0366D6");
            SetThemeBrush("VsDiffMetaBg", isDark ? "#252526" : "#F8F9FA");
            SetThemeBrush("VsDiffMetaFg", isDark ? "#8B949E" : "#57606A");
            SetThemeBrush("DiffAddedForeground", isDark ? "#89D185" : "#1A7F37");
            SetThemeBrush("DiffAddedBackground", isDark ? "#1B3B24" : "#E6FFEC");
            SetThemeBrush("DiffDeletedForeground", isDark ? "#F85149" : "#CF222E");
            SetThemeBrush("DiffDeletedBackground", isDark ? "#471C1E" : "#FFEBE9");
            SetThemeBrush("DiffHunkForeground", isDark ? "#79C0FF" : "#0366D6");
            SetThemeBrush("DiffHunkBackground", isDark ? "#1C2A3A" : "#F1F8FF");
            SetThemeBrush("DiffHeaderForeground", isDark ? "#8B949E" : "#57606A");
            SetThemeBrush("DiffHeaderBackground", isDark ? "#252526" : "#F8F9FA");
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
            viewer.Document.PageWidth = 2500;
            viewer.Document.FontFamily = new FontFamily("Consolas");
            viewer.Document.FontSize = 12;

            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string line in lines)
            {
                string foregroundKey = "VsTextBrush";
                string backgroundKey = null;

                if (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal) ||
                    line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal) ||
                    line.StartsWith("new file mode ", StringComparison.Ordinal) || line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
                    line.StartsWith("old mode ", StringComparison.Ordinal) || line.StartsWith("new mode ", StringComparison.Ordinal) ||
                    line.StartsWith("rename from ", StringComparison.Ordinal) || line.StartsWith("rename to ", StringComparison.Ordinal))
                {
                    foregroundKey = "VsDiffMetaFg";
                    backgroundKey = "VsDiffMetaBg";
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    foregroundKey = "VsDiffHunkFg";
                    backgroundKey = "VsDiffHunkBg";
                }
                else if (line.StartsWith("+", StringComparison.Ordinal))
                {
                    foregroundKey = "VsDiffAddedFg";
                    backgroundKey = "VsDiffAddedBg";
                }
                else if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    foregroundKey = "VsDiffDeletedFg";
                    backgroundKey = "VsDiffDeletedBg";
                }

                var paragraph = new Paragraph(new Run(line))
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(8, 1, 8, 1),
                    LineHeight = 16,
                    Foreground = (Brush)FindResource(foregroundKey)
                };

                if (backgroundKey != null)
                {
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
            var dialog = new NativeFolderDialog
            {
                Title = "Select a Git repository or project folder",
                InitialDirectory = Directory.Exists(RepositoryPathBox.Text)
                    ? RepositoryPathBox.Text
                    : Environment.CurrentDirectory
            };

            if (!dialog.ShowDialog(Window.GetWindow(this)))
                return;

            SetRepositoryPath(dialog.SelectedPath);
            await RefreshAllAsync();
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

        public async Task RefreshAllAsync()
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

                // 1. Parse Branch & Ahead/Behind
                string branchLine = (statusTask.Result.StandardOutput ?? string.Empty)
                    .Split(new[] { '\0', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? string.Empty;
                if (branchLine.StartsWith("## ", StringComparison.Ordinal))
                {
                    string info = branchLine.Substring(3).Trim();
                    string bName = info;
                    int dots = info.IndexOf("...", StringComparison.Ordinal);
                    if (dots >= 0) bName = info.Substring(0, dots).Trim();
                    else
                    {
                        int sp = info.IndexOf(' ');
                        if (sp >= 0) bName = info.Substring(0, sp).Trim();
                    }

                    int ahead = 0;
                    int behind = 0;
                    var aheadMatch = Regex.Match(info, @"ahead (\d+)");
                    if (aheadMatch.Success) int.TryParse(aheadMatch.Groups[1].Value, out ahead);
                    var behindMatch = Regex.Match(info, @"behind (\d+)");
                    if (behindMatch.Success) int.TryParse(behindMatch.Groups[1].Value, out behind);

                    _currentBranchName = string.IsNullOrEmpty(bName) ? "HEAD" : bName;
                    _aheadCount = ahead;
                    _behindCount = behind;

                    BranchSummary.Text = _currentBranchName;
                    BranchStatusButtonText.Text = "⑂ " + _currentBranchName;

                    if (ahead > 0 || behind > 0)
                    {
                        SyncPill.Visibility = Visibility.Visible;
                        SyncText.Text = string.Format("↓{0} ↑{1}", behind, ahead);
                        SyncButton.Content = string.Format("↻ Sync {0}↓ {1}↑", behind, ahead);
                    }
                    else
                    {
                        SyncPill.Visibility = Visibility.Collapsed;
                        SyncButton.Content = "↻ Sync";
                    }
                }
                else
                {
                    BranchSummary.Text = "Repository";
                    BranchStatusButtonText.Text = "⑂ Repository";
                    SyncPill.Visibility = Visibility.Collapsed;
                    SyncButton.Content = "↻ Sync";
                }

                // 2. Parse File Changes into VS Code Staged & Unstaged lists
                var rawChanges = GitRepositoryService.ParseStatus(statusTask.Result.StandardOutput);
                var newStaged = new List<VsCodeChangeItem>();
                var newUnstaged = new List<VsCodeChangeItem>();

                foreach (var c in rawChanges)
                {
                    string dir = Path.GetDirectoryName(c.Path);
                    if (!string.IsNullOrEmpty(c.OriginalPath))
                    {
                        dir = (string.IsNullOrEmpty(dir) ? "" : dir + " ") + "← " + c.OriginalPath;
                    }

                    // Staged changes (IndexStatus != ' ' and != '?')
                    if (c.IndexStatus != ' ' && c.IndexStatus != '?')
                    {
                        newStaged.Add(new VsCodeChangeItem
                        {
                            Path = c.Path,
                            OriginalPath = c.OriginalPath,
                            FileName = Path.GetFileName(c.Path),
                            DirectoryDisplay = dir,
                            StatusLetter = c.IndexStatus.ToString(),
                            StatusTooltip = GetStatusTooltip(c.IndexStatus, true),
                            IsStaged = true,
                            IsUntracked = false,
                            SourceChange = c
                        });
                    }

                    // Working Tree / Unstaged changes (WorkTreeStatus != ' ' or untracked)
                    if (c.WorkTreeStatus != ' ' || c.IsUntracked)
                    {
                        newUnstaged.Add(new VsCodeChangeItem
                        {
                            Path = c.Path,
                            OriginalPath = c.OriginalPath,
                            FileName = Path.GetFileName(c.Path),
                            DirectoryDisplay = dir,
                            StatusLetter = c.IsUntracked ? "U" : c.WorkTreeStatus.ToString(),
                            StatusTooltip = c.IsUntracked ? "Untracked" : GetStatusTooltip(c.WorkTreeStatus, false),
                            IsStaged = false,
                            IsUntracked = c.IsUntracked,
                            SourceChange = c
                        });
                    }
                }

                _stagedItems = newStaged;
                _unstagedItems = newUnstaged;

                StagedListBox.ItemsSource = _stagedItems;
                UnstagedListBox.ItemsSource = _unstagedItems;
                StagedCountText.Text = _stagedItems.Count.ToString();
                UnstagedCountText.Text = _unstagedItems.Count.ToString();

                StagedSection.Visibility = _stagedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                StagedListBox.Visibility = (_stagedItems.Count > 0 && _isStagedExpanded) ? Visibility.Visible : Visibility.Collapsed;
                UnstagedSection.Visibility = Visibility.Visible;
                UnstagedListBox.Visibility = _isUnstagedExpanded ? Visibility.Visible : Visibility.Collapsed;
                NoChangesPanel.Visibility = (_stagedItems.Count == 0 && _unstagedItems.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                NoRepositoryPanel.Visibility = Visibility.Collapsed;

                // 3. Branches, History & Remotes
                _branches = ParseBranches(branchTask.Result.StandardOutput);
                BranchesList.ItemsSource = _branches;
                HistoryList.ItemsSource = GitRepositoryService.ParseLog(logTask.Result.StandardOutput);
                RemotesText.Text = remoteTask.Result.Succeeded ? remoteTask.Result.StandardOutput.TrimEnd() : remoteTask.Result.CombinedOutput;

                RepositoryStatusText.Text = Path.GetFileName(_repository.RepositoryPath);
                RepositoryStatusText.ToolTip = _repository.RepositoryPath;
                StatusText.Text = "Repository refreshed";
                RepositoryRefreshed?.Invoke(this, EventArgs.Empty);

                // Retain selection or show prompt
                if (_currentSelectedChange != null)
                {
                    var match = _currentSelectedChange.IsStaged
                        ? _stagedItems.FirstOrDefault(i => i.Path == _currentSelectedChange.Path)
                        : _unstagedItems.FirstOrDefault(i => i.Path == _currentSelectedChange.Path);

                    if (match != null)
                    {
                        if (match.IsStaged) StagedListBox.SelectedItem = match;
                        else UnstagedListBox.SelectedItem = match;
                    }
                    else
                    {
                        ResetDiffViewer();
                    }
                }
                else
                {
                    ResetDiffViewer();
                }

                SetDiffContent(HistoryDiffViewer, string.Empty);
                CommitFilesList.ItemsSource = null;
            }
            catch (Exception exception)
            {
                SetNotRepository(exception.Message);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void ResetDiffViewer()
        {
            _currentSelectedChange = null;
            DiffTitle.Text = "Select a file to inspect diff";
            DiffPath.Text = string.Empty;
            DiffStageButton.Visibility = Visibility.Collapsed;
            DiffDiscardButton.Visibility = Visibility.Collapsed;
            DiffUnstageButton.Visibility = Visibility.Collapsed;
            SetDiffContent(DiffViewer, "Select a changed file in the sidebar to view its diff.");
        }

        private void SetNotRepository(string message)
        {
            BranchSummary.Text = "No repository";
            BranchStatusButtonText.Text = "⑂ No repository";
            SyncPill.Visibility = Visibility.Collapsed;
            NoRepositoryPanel.Visibility = Visibility.Visible;
            StagedListBox.ItemsSource = null;
            UnstagedListBox.ItemsSource = null;
            BranchesList.ItemsSource = null;
            HistoryList.ItemsSource = null;
            CommitFilesList.ItemsSource = null;
            ResetDiffViewer();
            SetDiffContent(DiffViewer, "This folder is not a Git repository. Click Initialize Repository or Open Folder to begin.");
            SetDiffContent(HistoryDiffViewer, string.Empty);
            RemotesText.Clear();
            RepositoryStatusText.Text = message;
            StatusText.Text = message;
        }

        private static string GetStatusTooltip(char code, bool isStaged)
        {
            string suffix = isStaged ? " (Index)" : " (Working Tree)";
            switch (code)
            {
                case 'M': return "Modified" + suffix;
                case 'A': return "Added" + suffix;
                case 'D': return "Deleted" + suffix;
                case 'R': return "Renamed" + suffix;
                case 'C': return "Copied" + suffix;
                case '?': return "Untracked";
                default: return "Changed" + suffix;
            }
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

        // ==========================
        // SELECTION & DIFF LOGIC
        // ==========================
        private void StagedListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing || _isSyncingSelection) return;
            var item = StagedListBox.SelectedItem as VsCodeChangeItem;
            if (item == null) return;

            _isSyncingSelection = true;
            try { UnstagedListBox.SelectedItem = null; }
            finally { _isSyncingSelection = false; }

            SelectChangeItem(item);
        }

        private void UnstagedListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing || _isSyncingSelection) return;
            var item = UnstagedListBox.SelectedItem as VsCodeChangeItem;
            if (item == null) return;

            _isSyncingSelection = true;
            try { StagedListBox.SelectedItem = null; }
            finally { _isSyncingSelection = false; }

            SelectChangeItem(item);
        }

        private async void SelectChangeItem(VsCodeChangeItem item)
        {
            _currentSelectedChange = item;
            if (item == null || _isBusy) return;

            int requestId = ++_diffRequestId;
            string path = item.Path;
            DiffTitle.Text = item.FileName + (item.IsStaged ? " (Index)" : " (Working Tree)");
            DiffPath.Text = item.Path;

            DiffStageButton.Visibility = !item.IsStaged ? Visibility.Visible : Visibility.Collapsed;
            DiffDiscardButton.Visibility = !item.IsStaged ? Visibility.Visible : Visibility.Collapsed;
            DiffUnstageButton.Visibility = item.IsStaged ? Visibility.Visible : Visibility.Collapsed;

            try
            {
                string diffOutput = string.Empty;
                if (item.IsStaged)
                {
                    var staged = await _repository.RunAsync("diff", "--cached", "--no-ext-diff", "--no-color", "--", path);
                    diffOutput = staged.StandardOutput;
                    if (string.IsNullOrWhiteSpace(diffOutput))
                    {
                        var show = await _repository.RunAsync("show", "--no-ext-diff", "--no-color", ":" + path);
                        if (show.Succeeded && !string.IsNullOrWhiteSpace(show.StandardOutput))
                        {
                            diffOutput = "+ Newly added file staged in index\n" + show.StandardOutput;
                        }
                        else
                        {
                            diffOutput = "No text diff available for staged file.";
                        }
                    }
                }
                else
                {
                    if (item.IsUntracked)
                    {
                        string absolutePath = Path.GetFullPath(Path.Combine(_repository.RepositoryPath, path));
                        var untracked = await _repository.RunAsync("diff", "--no-index", "--no-color", "--", "/dev/null", absolutePath);
                        if (!string.IsNullOrWhiteSpace(untracked.StandardOutput))
                        {
                            diffOutput = untracked.StandardOutput;
                        }
                        else if (File.Exists(absolutePath))
                        {
                            try
                            {
                                string fileContent = File.ReadAllText(absolutePath);
                                var lines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                diffOutput = "@@ -0,0 +1," + lines.Length + " @@ (Untracked file)\n" +
                                             string.Join("\n", lines.Select(l => "+" + l));
                            }
                            catch
                            {
                                diffOutput = "Untracked file. Click '+' to stage it.";
                            }
                        }
                        else
                        {
                            diffOutput = "Untracked file. Click '+' to stage it.";
                        }
                    }
                    else
                    {
                        var unstaged = await _repository.RunAsync("diff", "--no-ext-diff", "--no-color", "--", path);
                        diffOutput = unstaged.StandardOutput;
                        if (string.IsNullOrWhiteSpace(diffOutput))
                        {
                            diffOutput = "No working tree changes detected for this file.";
                        }
                    }
                }

                if (requestId == _diffRequestId && _currentSelectedChange == item)
                {
                    SetDiffContent(DiffViewer, diffOutput);
                }
            }
            catch (Exception ex)
            {
                if (requestId == _diffRequestId)
                {
                    SetDiffContent(DiffViewer, "Error reading diff: " + ex.Message);
                }
            }
        }

        // ==========================
        // ACCORDION EXPAND/COLLAPSE
        // ==========================
        private void StagedHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _isStagedExpanded = !_isStagedExpanded;
            StagedChevron.Text = _isStagedExpanded ? "▼" : "▶";
            StagedListBox.Visibility = _isStagedExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UnstagedHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _isUnstagedExpanded = !_isUnstagedExpanded;
            UnstagedChevron.Text = _isUnstagedExpanded ? "▼" : "▶";
            UnstagedListBox.Visibility = _isUnstagedExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==========================
        // INLINE STAGE / UNSTAGE / DISCARD
        // ==========================
        private async void StageItem_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as VsCodeChangeItem;
            if (item == null) return;
            var result = await RunCommandAsync("Stage file", "add", "--", item.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void UnstageItem_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as VsCodeChangeItem;
            if (item == null) return;
            var result = await RunCommandAsync("Unstage file", "restore", "--staged", "--", item.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void DiscardItem_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as VsCodeChangeItem;
            if (item == null) return;

            string action = item.IsUntracked ? "Delete the untracked file" : "Discard working tree changes to";
            if (MessageBox.Show(Window.GetWindow(this), action + "\n\n" + item.Path + "\n\nThis cannot be undone.", "Discard Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var result = item.IsUntracked
                ? await RunCommandAsync("Delete untracked file", "clean", "-fd", "--", item.Path)
                : await RunCommandAsync("Discard changes", "restore", "--worktree", "--", item.Path);

            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void StageAll_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var result = await RunCommandAsync("Stage all changes", "add", "-A");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void UnstageAll_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var result = await RunCommandAsync("Unstage all changes", "restore", "--staged", ".");
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void DiscardAll_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (MessageBox.Show(Window.GetWindow(this), "Are you sure you want to discard ALL changes in the working tree?\n\nThis cannot be undone.", "Discard All Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var restoreResult = await RunCommandAsync("Discard working changes", "restore", "--worktree", ".");
            await _repository.RunAsync("clean", "-fd");
            await RefreshAllAsync();
        }

        // ==========================
        // DIFF HEADER ACTIONS
        // ==========================
        private async void DiffStageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedChange == null) return;
            var result = await RunCommandAsync("Stage file", "add", "--", _currentSelectedChange.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void DiffDiscardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedChange == null) return;
            string action = _currentSelectedChange.IsUntracked ? "Delete the untracked file" : "Discard working tree changes to";
            if (MessageBox.Show(Window.GetWindow(this), action + "\n\n" + _currentSelectedChange.Path + "\n\nThis cannot be undone.", "Discard Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var result = _currentSelectedChange.IsUntracked
                ? await RunCommandAsync("Delete untracked file", "clean", "-fd", "--", _currentSelectedChange.Path)
                : await RunCommandAsync("Discard changes", "restore", "--worktree", "--", _currentSelectedChange.Path);

            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        private async void DiffUnstageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedChange == null) return;
            var result = await RunCommandAsync("Unstage file", "restore", "--staged", "--", _currentSelectedChange.Path);
            if (result != null && result.Succeeded) await RefreshAllAsync();
        }

        // ==========================
        // COMMIT LOGIC
        // ==========================
        private void CommitMessageBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CommitPlaceholder.Visibility = string.IsNullOrEmpty(CommitMessageBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CommitMessageBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await ExecuteCommitAsync();
            }
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCommitAsync();
        }

        private async Task ExecuteCommitAsync()
        {
            string message = CommitMessageBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                StatusText.Text = "Please provide a commit message.";
                CommitMessageBox.Focus();
                return;
            }

            if (_stagedItems.Count == 0)
            {
                if (_unstagedItems.Count == 0)
                {
                    StatusText.Text = "There are no changes to commit.";
                    return;
                }

                // VS Code prompt behavior: ask to stage all and commit directly
                var confirm = MessageBox.Show(Window.GetWindow(this),
                    "There are no staged changes to commit.\n\nWould you like to stage all your changes and commit them directly?",
                    "Source Control", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;

                var addResult = await RunCommandAsync("Stage all changes", "add", "-A");
                if (addResult == null || !addResult.Succeeded) return;
            }

            var result = await RunCommandAsync("Commit", "commit", "-m", message);
            if (result != null && result.Succeeded)
            {
                CommitMessageBox.Clear();
                await RefreshAllAsync();
            }
        }

        // ==========================
        // NAVIGATION & VIEWS
        // ==========================
        private void NavButton_Checked(object sender, RoutedEventArgs e)
        {
            if (ChangesViewGrid == null) return;
            ChangesViewGrid.Visibility = (NavChangesButton.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            CommitsViewGrid.Visibility = (NavCommitsButton.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            BranchesViewGrid.Visibility = (NavBranchesButton.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            TerminalViewGrid.Visibility = (NavTerminalButton.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==========================
        // STATUS BAR & MENUS
        // ==========================
        private void BranchStatusButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            if (_branches != null && _branches.Count > 0)
            {
                var localBranches = _branches.Where(b => !b.IsRemote).ToList();
                foreach (var b in localBranches)
                {
                    var item = new MenuItem
                    {
                        Header = (b.IsCurrent ? "✓  " : "    ") + b.Name,
                        FontWeight = b.IsCurrent ? FontWeights.Bold : FontWeights.Normal,
                        Tag = b
                    };
                    item.Click += async (s, ev) =>
                    {
                        var target = (s as MenuItem)?.Tag as GitBranch;
                        if (target != null && !target.IsCurrent)
                        {
                            var result = await RunCommandAsync("Switch branch", "switch", target.Name);
                            if (result != null && result.Succeeded) await RefreshAllAsync();
                        }
                    };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
            }

            var createItem = new MenuItem { Header = "+ Create New Branch..." };
            createItem.Click += (s, ev) =>
            {
                NavBranchesButton.IsChecked = true;
                NewBranchBox.Focus();
            };
            menu.Items.Add(createItem);

            var manageItem = new MenuItem { Header = "Manage Branches & Remotes..." };
            manageItem.Click += (s, ev) => { NavBranchesButton.IsChecked = true; };
            menu.Items.Add(manageItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            var pullResult = await RunCommandAsync("Pull", "pull");
            if (pullResult == null || !pullResult.Succeeded) return;
            var pushResult = await RunCommandAsync("Push", "push");
            if (pushResult != null && pushResult.Succeeded) await RefreshAllAsync();
        }

        private void MoreActions_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var syncItem = new MenuItem { Header = "Sync Changes (Pull & Push)" };
            syncItem.Click += SyncButton_Click;
            menu.Items.Add(syncItem);

            var pullItem = new MenuItem { Header = "Pull" };
            pullItem.Click += Pull_Click;
            menu.Items.Add(pullItem);

            var pushItem = new MenuItem { Header = "Push" };
            pushItem.Click += Push_Click;
            menu.Items.Add(pushItem);

            var fetchItem = new MenuItem { Header = "Fetch (All)" };
            fetchItem.Click += Fetch_Click;
            menu.Items.Add(fetchItem);

            menu.Items.Add(new Separator());

            var openFolderItem = new MenuItem { Header = "Open Repository Folder..." };
            openFolderItem.Click += OpenRepository_Click;
            menu.Items.Add(openFolderItem);

            var initItem = new MenuItem { Header = "Initialize Repository..." };
            initItem.Click += InitializeRepository_Click;
            menu.Items.Add(initItem);

            var cloneItem = new MenuItem { Header = "Clone Repository..." };
            cloneItem.Click += (s, ev) => NavBranchesButton.IsChecked = true;
            menu.Items.Add(cloneItem);

            menu.Items.Add(new Separator());

            var terminalItem = new MenuItem { Header = "Open Git Terminal..." };
            terminalItem.Click += (s, ev) => NavTerminalButton.IsChecked = true;
            menu.Items.Add(terminalItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
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

        // ==========================
        // COMMITS / HISTORY
        // ==========================
        private async void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        private void CommitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        // ==========================
        // BRANCHES & REMOTES
        // ==========================
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

        // ==========================
        // TERMINAL / COMMAND
        // ==========================
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
