using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Gitify.Engine;

namespace Typify
{
    public class GitWorkspaceItem
    {
        public string Name { get; set; }
        public string RepositoryRoot { get; set; }
        public GitRepositoryService Service { get; set; }
        public string CurrentBranch { get; set; }
        public int StagedCount { get; set; }
        public int UnstagedCount { get; set; }
        public int TotalChanges => StagedCount + UnstagedCount;

        public string BranchSummary => string.IsNullOrEmpty(CurrentBranch) ? string.Empty : "⑂ " + CurrentBranch;
        public string ChangesSummary => TotalChanges == 0 ? "clean" : TotalChanges + " change" + (TotalChanges == 1 ? "" : "s");
        public Visibility HasChangesVisibility => TotalChanges > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string TotalChangesText => TotalChanges.ToString();

        public string DraftSubject { get; set; } = string.Empty;
        public string DraftBody { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    public partial class GitCommitOverlay : UserControl
    {
        private readonly List<GitWorkspaceItem> _workspaces = new List<GitWorkspaceItem>();
        private GitWorkspaceItem _selectedWorkspace;
        private GitRepositoryService _repository;
        private bool _isDarkTheme;
        private bool _isBusy;
        private string _originalPreAmendSubject;
        private string _originalPreAmendBody;
        private List<VsCodeChangeItem> _stagedItems = new List<VsCodeChangeItem>();
        private List<VsCodeChangeItem> _unstagedItems = new List<VsCodeChangeItem>();
        private bool _isStagedExpanded = true;
        private bool _isUnstagedExpanded = true;
        private bool _isDescriptionOpen = false;
        private Effect _floatingEffect;

        public event EventHandler CommitCompleted;
        public event EventHandler StatusUpdated;
        public event EventHandler VisibilityChanged;
        public event EventHandler DockToggleRequested;
        public event EventHandler CloseDockedRequested;
        public event EventHandler AddFolderRequested;

        public int StagedCount => _workspaces.Count > 0 ? _workspaces.Sum(w => w.StagedCount) : (_stagedItems?.Count ?? 0);
        public IReadOnlyList<GitWorkspaceItem> Workspaces => _workspaces.AsReadOnly();
        public GitWorkspaceItem SelectedWorkspace => _selectedWorkspace;
        public bool IsOpen => Visibility == Visibility.Visible;

        private bool _isDocked = false;
        public bool IsDocked
        {
            get { return _isDocked; }
            set { _isDocked = value; TabHeaderBar.CornerRadius = value ? new CornerRadius(0) : new CornerRadius(6, 6, 0, 0); }
        }

        public GitCommitOverlay()
        {
            InitializeComponent();
            _floatingEffect = OverlayWindowCard.Effect;
            ApplyTheme(false);
            UpdateDirtyState();
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

        public async Task SetWorkspacesAsync(IEnumerable<string> workspacePaths, bool? isDarkTheme = null)
        {
            if (isDarkTheme.HasValue)
            {
                _isDarkTheme = isDarkTheme.Value;
                ApplyTheme(_isDarkTheme);
            }

            if (workspacePaths == null)
            {
                workspacePaths = Enumerable.Empty<string>();
            }

            var validRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in workspacePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;

                try
                {
                    var probe = new GitRepositoryService(path);
                    var revResult = await probe.RunAsync("rev-parse", "--show-toplevel");
                    if (revResult.Succeeded && !string.IsNullOrWhiteSpace(revResult.StandardOutput))
                    {
                        string root = Path.GetFullPath(revResult.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar));
                        if (Directory.Exists(root))
                        {
                            validRoots.Add(root);
                        }
                    }
                }
                catch
                {
                    // Ignore directories that are not Git repositories
                }
            }

            // Sync with existing list to keep drafts and references
            var updatedList = new List<GitWorkspaceItem>();
            foreach (string root in validRoots)
            {
                var existing = _workspaces.FirstOrDefault(w => string.Equals(w.RepositoryRoot, root, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    updatedList.Add(existing);
                }
                else
                {
                    string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(name)) name = root;

                    updatedList.Add(new GitWorkspaceItem
                    {
                        Name = name,
                        RepositoryRoot = root,
                        Service = new GitRepositoryService(root),
                        CurrentBranch = "main"
                    });
                }
            }

            _workspaces.Clear();
            _workspaces.AddRange(updatedList);

            if (_workspaces.Count == 0)
            {
                _selectedWorkspace = null;
                _repository = null;
                _stagedItems.Clear();
                _unstagedItems.Clear();
                StagedFilesListBox.ItemsSource = null;
                UnstagedFilesListBox.ItemsSource = null;
                RepositoryRow.Visibility = Visibility.Collapsed;
                NoRepositoriesNotice.Visibility = Visibility.Visible;
                CommitScrollViewer.Visibility = Visibility.Collapsed;
                BranchLabel.Text = "No repo";
                TitleText.Text = "Git Changes";
                StatusUpdated?.Invoke(this, EventArgs.Empty);
                return;
            }

            NoRepositoriesNotice.Visibility = Visibility.Collapsed;
            CommitScrollViewer.Visibility = Visibility.Visible;

            if (_selectedWorkspace == null || !_workspaces.Any(w => string.Equals(w.RepositoryRoot, _selectedWorkspace.RepositoryRoot, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedWorkspace = _workspaces[0];
            }
            else
            {
                _selectedWorkspace = _workspaces.First(w => string.Equals(w.RepositoryRoot, _selectedWorkspace.RepositoryRoot, StringComparison.OrdinalIgnoreCase));
            }

            _repository = _selectedWorkspace.Service;

            if (_workspaces.Count > 1)
            {
                RepositoryRow.Visibility = Visibility.Visible;
                TitleText.Text = string.Format("Git Changes - {0} ({1} Repositories)", _selectedWorkspace.Name, _workspaces.Count);
            }
            else
            {
                RepositoryRow.Visibility = Visibility.Collapsed;
                TitleText.Text = "Git Changes - " + _selectedWorkspace.Name;
            }

            RepositoryComboBox.SelectionChanged -= RepositoryComboBox_SelectionChanged;
            RepositoryComboBox.ItemsSource = null;
            RepositoryComboBox.ItemsSource = _workspaces;
            RepositoryComboBox.SelectedItem = _selectedWorkspace;
            RepositoryComboBox.SelectionChanged += RepositoryComboBox_SelectionChanged;

            await RefreshStatusAsync();
        }

        public async Task InitializeAsync(string repositoryPath, bool isDarkTheme)
        {
            _isDarkTheme = isDarkTheme;
            ApplyTheme(isDarkTheme);
            var list = string.IsNullOrWhiteSpace(repositoryPath) ? Enumerable.Empty<string>() : new[] { repositoryPath };
            await SetWorkspacesAsync(list, isDarkTheme);
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
            SetThemeBrush("VsStatusRenamedBrush", isDark ? "#4EC9B0" : "#0969DA");

            SetThemeBrush("VsBadgeBrush", isDark ? "#3A3D41" : "#E1E1E1");
            SetThemeBrush("VsBadgeTextBrush", isDark ? "#CCCCCC" : "#333333");
        }

        private void SetThemeBrush(string key, string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        public async Task RefreshStatusAsync()
        {
            if (_repository == null || _isBusy) return;

            try
            {
                var branchTask = _repository.RunAsync("symbolic-ref", "--short", "HEAD");
                var statusTask = _repository.RunAsync("status", "--porcelain=v1", "-z");

                await Task.WhenAll(branchTask, statusTask);

                string branch = branchTask.Result.Succeeded ? branchTask.Result.StandardOutput.Trim() : "HEAD (detached)";
                BranchLabel.Text = string.IsNullOrEmpty(branch) ? "main" : branch;

                var changes = GitRepositoryService.ParseStatus(statusTask.Result.StandardOutput);
                var staged = new List<VsCodeChangeItem>();
                var unstaged = new List<VsCodeChangeItem>();

                foreach (var c in changes)
                {
                    string dir = Path.GetDirectoryName(c.Path);
                    if (!string.IsNullOrEmpty(c.OriginalPath))
                    {
                        dir = (string.IsNullOrEmpty(dir) ? "" : dir + " ") + "← " + c.OriginalPath;
                    }

                    if (c.IndexStatus != ' ' && c.IndexStatus != '?')
                    {
                        staged.Add(new VsCodeChangeItem
                        {
                            Path = c.Path,
                            OriginalPath = c.OriginalPath,
                            FileName = Path.GetFileName(c.Path),
                            DirectoryDisplay = dir,
                            StatusLetter = c.IndexStatus.ToString(),
                            StatusTooltip = "Staged: " + c.Status,
                            IsStaged = true,
                            IsUntracked = false,
                            SourceChange = c
                        });
                    }

                    if (c.WorkTreeStatus != ' ' || c.IsUntracked)
                    {
                        unstaged.Add(new VsCodeChangeItem
                        {
                            Path = c.Path,
                            OriginalPath = c.OriginalPath,
                            FileName = Path.GetFileName(c.Path),
                            DirectoryDisplay = dir,
                            StatusLetter = c.IsUntracked ? "U" : c.WorkTreeStatus.ToString(),
                            StatusTooltip = c.IsUntracked ? "Untracked" : "Modified",
                            IsStaged = false,
                            IsUntracked = c.IsUntracked,
                            SourceChange = c
                        });
                    }
                }

                _stagedItems = staged;
                _unstagedItems = unstaged;

                if (_selectedWorkspace != null)
                {
                    _selectedWorkspace.CurrentBranch = branch;
                    _selectedWorkspace.StagedCount = _stagedItems.Count;
                    _selectedWorkspace.UnstagedCount = _unstagedItems.Count;
                }

                StagedFilesListBox.ItemsSource = _stagedItems;
                UnstagedFilesListBox.ItemsSource = _unstagedItems;

                StagedCountBracket.Text = string.Format(" ({0})", _stagedItems.Count);
                UnstagedCountBracket.Text = string.Format(" ({0})", _unstagedItems.Count);

                ZeroStagedNotice.Visibility = _stagedItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                NoUnstagedNotice.Visibility = _unstagedItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UnstagedFilesListBox.Visibility = (_unstagedItems.Count > 0 && _isUnstagedExpanded) ? Visibility.Visible : Visibility.Collapsed;
                StagedFilesListBox.Visibility = (_stagedItems.Count > 0 && _isStagedExpanded) ? Visibility.Visible : Visibility.Collapsed;

                ExecuteCommitButton.Content = _stagedItems.Count > 0 ? "Commit Staged" : "Commit All";

                // Background probe other workspaces to keep badges up to date
                if (_workspaces.Count > 1)
                {
                    var otherWorkspaces = _workspaces.Where(w => w != _selectedWorkspace).ToList();
                    var probeTasks = otherWorkspaces.Select(async other =>
                    {
                        try
                        {
                            var oBranchTask = other.Service.RunAsync("symbolic-ref", "--short", "HEAD");
                            var oStatusTask = other.Service.RunAsync("status", "--porcelain=v1", "-z");
                            await Task.WhenAll(oBranchTask, oStatusTask);
                            if (oBranchTask.Result.Succeeded)
                            {
                                other.CurrentBranch = oBranchTask.Result.StandardOutput.Trim();
                            }
                            var oChanges = GitRepositoryService.ParseStatus(oStatusTask.Result.StandardOutput);
                            other.StagedCount = oChanges.Count(c => c.IndexStatus != ' ' && c.IndexStatus != '?');
                            other.UnstagedCount = oChanges.Count(c => c.WorkTreeStatus != ' ' || c.IsUntracked);
                        }
                        catch { }
                    });
                    await Task.WhenAll(probeTasks);
                }

                RepositoryComboBox.Items.Refresh();
                StatusUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ShowNotification("Error refreshing git: " + ex.Message);
            }
        }

        private async void RepositoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RepositoryComboBox.SelectedItem is GitWorkspaceItem selected && selected != _selectedWorkspace)
            {
                if (_selectedWorkspace != null)
                {
                    _selectedWorkspace.DraftSubject = SubjectTextBox.Text ?? string.Empty;
                    _selectedWorkspace.DraftBody = BodyTextBox.Text ?? string.Empty;
                }

                _selectedWorkspace = selected;
                _repository = selected.Service;

                SubjectTextBox.Text = selected.DraftSubject ?? string.Empty;
                BodyTextBox.Text = selected.DraftBody ?? string.Empty;
                if (!string.IsNullOrEmpty(selected.DraftBody))
                {
                    _isDescriptionOpen = true;
                    DescriptionToggleIcon.Text = "▾";
                    BodyTextBox.Visibility = Visibility.Visible;
                }

                TitleText.Text = _workspaces.Count > 1
                    ? string.Format("Git Changes - {0} ({1} Repositories)", selected.Name, _workspaces.Count)
                    : "Git Changes - " + selected.Name;

                await RefreshStatusAsync();
            }
        }

        private void AddFolderRootNotice_Click(object sender, RoutedEventArgs e)
        {
            AddFolderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowAndFocus()
        {
            Visibility = Visibility.Visible;
            OverlayWindowCard.Visibility = Visibility.Visible;
            SubjectTextBox.Focus();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
            var _ = RefreshStatusAsync();
        }

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
                Visibility = Visibility.Collapsed;
            else
                ShowAndFocus();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ShowNotification(string message)
        {
            NotificationBannerText.Text = message;
            NotificationBanner.Visibility = Visibility.Visible;
        }

        private void DismissNotification_Click(object sender, RoutedEventArgs e)
        {
            NotificationBanner.Visibility = Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsDocked)
            {
                CloseDockedRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            Visibility = Visibility.Collapsed;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        // ==========================
        // INPUTS & CHAR COUNTER
        // ==========================
        private void SubjectTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = SubjectTextBox.Text ?? string.Empty;
            SubjectPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

            int len = text.Length;
            SubjectCharCounter.Text = len + "/50";

            if (len <= 50)
            {
                SubjectCharCounter.Foreground = (Brush)Resources["VsMutedTextBrush"];
            }
            else if (len <= 72)
            {
                SubjectCharCounter.Foreground = (Brush)Resources["VsWarningBrush"];
            }
            else
            {
                SubjectCharCounter.Foreground = (Brush)Resources["VsErrorBrush"];
            }

            UpdateDirtyState();
        }

        private void BodyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDirtyState();
        }

        private void ToggleDescriptionBox_Click(object sender, RoutedEventArgs e)
        {
            _isDescriptionOpen = !_isDescriptionOpen;
            DescriptionToggleIcon.Text = _isDescriptionOpen ? "▾" : "▸";
            BodyTextBox.Visibility = _isDescriptionOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_isDescriptionOpen) BodyTextBox.Focus();
        }

        private void UpdateDirtyState()
        {
            bool hasContent = !string.IsNullOrWhiteSpace(SubjectTextBox.Text) || !string.IsNullOrWhiteSpace(BodyTextBox.Text);
            DirtyIndicator.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CommitInputs_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await ExecuteCommitAsync(false);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseButton_Click(this, new RoutedEventArgs());
            }
        }

        // ==========================
        // OPTIONS ("und co")
        // ==========================
        private async void AmendCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isAmend = AmendCheckBox.IsChecked == true;
            ExecuteCommitButton.Content = isAmend ? "Commit (Amend)" : (_stagedItems.Count > 0 ? "Commit Staged" : "Commit All");

            if (isAmend)
            {
                _originalPreAmendSubject = SubjectTextBox.Text;
                _originalPreAmendBody = BodyTextBox.Text;

                if (_repository != null)
                {
                    try
                    {
                        var logResult = await _repository.RunAsync("log", "-1", "--pretty=%B");
                        if (logResult.Succeeded && !string.IsNullOrWhiteSpace(logResult.StandardOutput))
                        {
                            string rawMessage = logResult.StandardOutput.TrimEnd();
                            string[] lines = rawMessage.Replace("\r\n", "\n").Split('\n');
                            SubjectTextBox.Text = lines.Length > 0 ? lines[0] : "";
                            BodyTextBox.Text = lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1)).TrimStart('\r', '\n') : "";
                            if (!string.IsNullOrEmpty(BodyTextBox.Text))
                            {
                                _isDescriptionOpen = true;
                                DescriptionToggleIcon.Text = "▾";
                                BodyTextBox.Visibility = Visibility.Visible;
                            }
                            ShowNotification("Loaded message from HEAD commit.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowNotification("Could not load HEAD commit: " + ex.Message);
                    }
                }
            }
            else
            {
                SubjectTextBox.Text = _originalPreAmendSubject ?? string.Empty;
                BodyTextBox.Text = _originalPreAmendBody ?? string.Empty;
            }
        }

        private void AddCoAuthor_Click(object sender, RoutedEventArgs e)
        {
            const string template = "Co-authored-by: Name <name@example.com>";
            _isDescriptionOpen = true;
            DescriptionToggleIcon.Text = "▾";
            BodyTextBox.Visibility = Visibility.Visible;

            string current = BodyTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(current))
            {
                BodyTextBox.Text = template;
            }
            else
            {
                BodyTextBox.Text = current.TrimEnd() + Environment.NewLine + Environment.NewLine + template;
            }
            BodyTextBox.Focus();
            BodyTextBox.CaretIndex = BodyTextBox.Text.Length;
        }

        private void StagedSummaryHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _isStagedExpanded = !_isStagedExpanded;
            StagedExpanderIcon.Text = _isStagedExpanded ? "▾" : "▸";
            StagedFilesListBox.Visibility = (_isStagedExpanded && _stagedItems.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UnstagedSummaryHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _isUnstagedExpanded = !_isUnstagedExpanded;
            UnstagedExpanderIcon.Text = _isUnstagedExpanded ? "▾" : "▸";
            UnstagedFilesListBox.Visibility = (_isUnstagedExpanded && _unstagedItems.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void StageAllNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_repository == null || _isBusy) return;
            try
            {
                var result = await _repository.RunAsync("add", "-A");
                if (result.Succeeded)
                {
                    ShowNotification("All changes staged.");
                    await RefreshStatusAsync();
                }
                else
                {
                    ShowNotification("Failed to stage: " + result.CombinedOutput);
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error staging: " + ex.Message);
            }
        }

        private async void UnstageAll_Click(object sender, RoutedEventArgs e)
        {
            if (_repository == null || _isBusy) return;
            try
            {
                var result = await _repository.RunAsync("restore", "--staged", ".");
                if (result.Succeeded)
                {
                    ShowNotification("All changes unstaged.");
                    await RefreshStatusAsync();
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error unstaging: " + ex.Message);
            }
        }

        private async void StageSingleFile_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string path = button?.Tag as string;
            if (string.IsNullOrEmpty(path) || _repository == null || _isBusy) return;

            try
            {
                var result = await _repository.RunAsync("add", "--", path);
                if (result.Succeeded)
                {
                    await RefreshStatusAsync();
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error staging file: " + ex.Message);
            }
        }

        private async void UnstageFile_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string path = button?.Tag as string;
            if (string.IsNullOrEmpty(path) || _repository == null || _isBusy) return;

            try
            {
                var result = await _repository.RunAsync("restore", "--staged", "--", path);
                if (result.Succeeded)
                {
                    await RefreshStatusAsync();
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error unstaging file: " + ex.Message);
            }
        }

        // ==========================
        // SYNC ACTIONS: PULL / PUSH / REFRESH
        // ==========================
        private async void PullButton_Click(object sender, RoutedEventArgs e)
        {
            if (_repository == null || _isBusy) return;
            _isBusy = true;
            ShowNotification("Pulling changes from remote…");
            try
            {
                var result = await _repository.RunAsync("pull");
                ShowNotification(result.Succeeded ? "✓ Successfully pulled latest changes." : "Pull failed: " + result.CombinedOutput);
                await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                ShowNotification("Pull failed: " + ex.Message);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void PushButton_Click(object sender, RoutedEventArgs e)
        {
            if (_repository == null || _isBusy) return;
            _isBusy = true;
            ShowNotification("Pushing changes to remote…");
            try
            {
                var result = await _repository.RunAsync("push");
                ShowNotification(result.Succeeded ? "✓ Successfully pushed to remote." : "Push failed: " + result.CombinedOutput);
                await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                ShowNotification("Push failed: " + ex.Message);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        // ==========================
        // COMMIT ACTIONS
        // ==========================
        private async void CommitButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCommitAsync(false);
        }

        private async void CommitAndPush_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCommitAsync(true);
        }

        private async Task ExecuteCommitAsync(bool pushAfterCommit)
        {
            if (_isBusy || _repository == null) return;

            string subject = (SubjectTextBox.Text ?? string.Empty).Trim();
            string body = (BodyTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(subject))
            {
                ShowNotification("Please enter a commit message.");
                SubjectTextBox.Focus();
                return;
            }

            _isBusy = true;
            OverlayWindowCard.IsEnabled = false;

            try
            {
                // Auto-stage if no staged changes exist
                if (_stagedItems.Count == 0 && AmendCheckBox.IsChecked != true)
                {
                    if (_unstagedItems.Count == 0)
                    {
                        ShowNotification("No changes to commit.");
                        return;
                    }

                    var confirm = MessageBox.Show(Window.GetWindow(this),
                        "There are no staged changes to commit.\n\nWould you like to stage all your changes and commit them directly?",
                        "Git Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes)
                    {
                        ShowNotification("Commit cancelled.");
                        return;
                    }

                    var addResult = await _repository.RunAsync("add", "-A");
                    if (!addResult.Succeeded)
                    {
                        ShowNotification("Failed to stage changes: " + addResult.CombinedOutput);
                        return;
                    }
                }

                var args = new List<string> { "commit" };
                if (AmendCheckBox.IsChecked == true) args.Add("--amend");
                if (SignOffCheckBox.IsChecked == true) args.Add("--signoff");

                args.Add("-m");
                args.Add(subject);

                if (!string.IsNullOrWhiteSpace(body))
                {
                    args.Add("-m");
                    args.Add(body);
                }

                var commitResult = await _repository.RunAsync(args);
                if (commitResult.Succeeded)
                {
                    SubjectTextBox.Clear();
                    BodyTextBox.Clear();
                    AmendCheckBox.IsChecked = false;
                    ExecuteCommitButton.Content = "Commit Staged";
                    if (_selectedWorkspace != null)
                    {
                        _selectedWorkspace.DraftSubject = string.Empty;
                        _selectedWorkspace.DraftBody = string.Empty;
                    }

                    // Fetch short commit hash
                    var revTask = await _repository.RunAsync("rev-parse", "--short", "HEAD");
                    string hash = revTask.Succeeded ? revTask.StandardOutput.Trim() : "";

                    string successNotice = string.IsNullOrEmpty(hash)
                        ? "Successfully committed."
                        : "Successfully committed [" + hash + "]: " + (subject.Length > 28 ? subject.Substring(0, 25) + "…" : subject);

                    if (pushAfterCommit)
                    {
                        ShowNotification(successNotice + " Pushing…");
                        var pushResult = await _repository.RunAsync("push");
                        if (pushResult.Succeeded)
                        {
                            ShowNotification("✓ " + successNotice + " & pushed to remote.");
                        }
                        else
                        {
                            ShowNotification(successNotice + " (Push failed: " + pushResult.CombinedOutput + ")");
                        }
                    }
                    else
                    {
                        ShowNotification("✓ " + successNotice);
                    }

                    await RefreshStatusAsync();
                    CommitCompleted?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ShowNotification("Commit failed: " + commitResult.CombinedOutput);
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error during commit: " + ex.Message);
            }
            finally
            {
                OverlayWindowCard.IsEnabled = true;
                _isBusy = false;
            }
        }
    }
}
