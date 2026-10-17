# Typify application manual

`Typify` is a Windows Presentation Foundation (WPF) desktop application targeting .NET Framework 4.7.2. Its main window combines a project explorer, Typst source editor, live preview and an integrated Git workspace. The application itself provides the shell and orchestration; Typst compilation and Git operations are performed by installed command-line tools.

## Contents

- [Architecture and source files](#architecture-and-source-files)
- [Requirements and setup](#requirements-and-setup)
- [Workspace and project explorer](#workspace-and-project-explorer)
- [Editing and live preview](#editing-and-live-preview)
- [PDF export](#pdf-export)
- [Git tab](#git-tab)
- [Themes and layout](#themes-and-layout)
- [Errors and troubleshooting](#errors-and-troubleshooting)
- [Current limitations](#current-limitations)

## Architecture and source files

| File | Responsibility |
| --- | --- |
| `MainWindow.xaml` | Main window layout, theme resources, Workspace/Git tabs, editor/explorer/preview controls. |
| `MainWindow.xaml.cs` | Startup, working-directory selection, theme switching, project tree, editor events, preview scheduling, and PDF export. |
| `LiveTypstPreview.cs` | Creates temporary Typst source/output paths, runs Typst to PNG, loads page images, collects diagnostics, cancels superseded work, and removes temporary files. |
| `GitTabView.xaml` | Git tab interface and its Changes, History, Branches & remotes, and Git command views. |
| `GitTabView.xaml.cs` | Repository discovery, Git tab actions, command output, branch/history refresh, and diff coloring. |
| `App.xaml`, `App.xaml.cs`, `App.config` | WPF application bootstrap and .NET Framework 4.7.2 runtime declaration. |

Project references:

- `TypifyTextbox/TypifyTextbox.csproj`: reusable editor control.
- `Gitify.Engine/Gitify.Engine.csproj`: Git process wrapper and common Git output models.

`TypstToPdfRenderer` is a separate executable and is not referenced by the main window.

## Requirements and setup

### Runtime

- Windows and the .NET Framework 4.7.2 runtime.
- `typst` executable on `PATH` for live preview and PDF export.
- `git` executable on `PATH` for the Git tab.

### Developer setup

Open `Typify.slnx` in a Visual Studio/MSBuild installation that supports this solution format and install the .NET Framework 4.7.2 developer pack. Build `Typify/Typify.csproj` or the full solution. The app is configured for `AnyCPU`; the WPF target framework is .NET Framework 4.7.2.

Before launching, verify that `typst --version` and `git --version` work in the environment inherited by Typify. The program does not bundle these executables or manage their installation.

### Working directory

The project root is stored as a private field in `MainWindow.xaml.cs`:

```csharp
// Set this value to the root folder of the Typst project.
private string _workingDirectory = GetDefaultWorkingDirectory();
```

To use a fixed directory, replace its initializer with an absolute path:

```csharp
private string _workingDirectory = @"D:\Documents\MyTypstProject";
```

The default helper combines the executable directory with `..\..\..` and uses that path when it contains `Typify.slnx`. If it cannot find that file, the executable directory is used. The explorer uses this value as its root. The live compiler passes it as `--root`; the Git tab uses it as the initial repository path.

The Git tab can later select another folder with **Open folder**. That selection lives in the current process only and is not written back to `_workingDirectory` or persisted.

## Workspace and project explorer

The **Workspace** tab has three resizable panes: Explorer, Source, and Preview. Grid splitters adjust the widths for the current window.

### Explorer behavior

- The root node is `_workingDirectory`. Press the refresh icon to rebuild the tree from the root.
- Directory children are enumerated only when a folder is expanded. Child folders appear before files; each group is sorted by name without case sensitivity.
- The tree does not filter `.git`, build output, hidden files, or `.gitignore` patterns. Large project roots may therefore contain many visible folders.
- The tree displays custom colored file icons based on common extensions (`.typ`, `.pdf`, images, data files, source code, markup, and text); other extensions use the generic icon.
- Full paths are available in node tooltips. Access-denied or unreadable folders produce a placeholder row.
- Double-clicking a `.typ` file reads it from disk and replaces the editor's current text. Other files can be browsed but are not opened in the editor.
- Preview source files whose names start with `TypifyPreview-` are hidden from file nodes because preview compilation uses temporary `.typ` files.

## Editing and live preview

The source pane hosts `Typify.TextBox.TypifyTextbox`. Its `Text` property is the current source buffer. Changes are observed by `MainWindow`; no editor file is written by the preview path.

### Compile pipeline

1. The editor raises `TextChanged` after source input or a loaded document changes.
2. `QueuePreviewCompile` stores the latest source, increments a version number, cancels any previous preview process, and restarts a 50 ms dispatcher timer.
3. After the timer, `LiveTypstPreview.CompileAsync` creates a unique temp directory under the OS temporary folder and writes the buffer to a uniquely named `TypifyPreview-*.typ` file.
4. If a `.typ` file was opened, the temporary source is placed in that file's containing directory so relative imports resolve from the same source folder. On initial startup, it is placed under the working directory.
5. The process runs the equivalent of:

   ```text
   typst compile --root <working-directory> --format png --ppi 72 --diagnostic-format short <temporary-source> <temp-directory>/page-{p}-of-{t}.png
   ```

6. Successful output PNG pages are loaded and frozen as WPF images. Pages are fit to the preview viewport. On failure, compiler output is displayed and the last successful pages remain in place, if any.
7. Temporary input and output are deleted in a `finally` cleanup path. A version check prevents stale results from replacing the output of more recent typing.

The cancellation token kills the active Typst process when another edit supersedes it or the window closes. Typst still needs time to start and compile, so the 50 ms debounce is only the delay before a compile request, not a guarantee that rendered output appears within 50 ms.

### Imports, assets, and Typst root

The configured working directory is supplied as Typst's `--root`. The source file's directory is used for the generated input document after a `.typ` file has been opened. Relative imports such as `#import "thesis-worms.typ": thesis-worms` and nearby assets can therefore resolve when they are inside the root and available in the source directory tree. Files outside the root may be rejected by Typst's root restrictions.

The application does not watch imported `.typ` files or asset files for external changes. Editing the main editor buffer triggers the next compile; modifying a dependency on disk alone does not.

### Compile failures

When Typst returns a non-zero exit code, its stderr/stdout text is shown in the diagnostics area. If the compiler executable cannot be found, the preview reports that `typst` must be installed and made available on `PATH`. If the working/source directory does not exist, the corresponding directory error is shown. The status label differentiates an initial failed compile from a failure while showing the last successful preview.

## PDF export

The header **Export PDF** action performs an explicit disk-to-disk compile:

1. Choose a Typst source file (`.typ`).
2. Choose a PDF destination. The dialog defaults to the source folder and source basename with `.pdf`.
3. Typify runs `typst compile --format pdf <selected-source> <selected-output>` with the source directory as the process working directory.
4. A successful run reports the output path. A failed run shows compiler output in an error dialog.

This action does not export the current editor buffer. To export edits, save them to a `.typ` file outside Typify first, then select that saved file in the dialog. The current application has no save command.

## Git tab

The Git tab is a WPF `UserControl` in the main window. At startup it initializes from `_workingDirectory`, normalizes the path, locates the repository top level with `git rev-parse --show-toplevel`, then refreshes status, branches, history, and remotes. If the selected folder is inside a repository, the displayed repository root becomes the repository path.

Use **Open folder** to select another existing folder. **Initialize** runs `git init` there. **Refresh** re-reads repository data. Actions use `Gitify.Engine.GitRepositoryService`; the engine's separate documentation describes its API and command process model.

### Changes view

- The branch summary comes from `git status --porcelain=v1 -z --branch`.
- The file list represents index and working-tree status, including untracked items and rename paths when reported by Git.
- Selecting a tracked item requests cached and/or uncached `git diff` output. Untracked items are diffed against an empty file where possible. The RichTextBox colorizes file headers, hunk headers, additions, and deletions.
- **Stage** runs `git add -- <path>` on the selected file.
- **Unstage** runs `git restore --staged -- <path>`.
- **Discard** confirms first. For an untracked file it uses `git clean -fd -- <path>` (which deletes it); for tracked files with work-tree changes it uses `git restore --worktree -- <path>`. It does not reset staged changes.
- **Commit** runs `git commit -m <message>`. It only commits already staged changes; the UI does not automatically stage files.
- **Fetch** runs `git fetch --all --prune`; **Pull** and **Push** run the plain `git pull` and `git push`, respecting the repository's configured defaults/upstream.

### History view

The first refresh requests at most 300 commits from `git log --all`, with ISO dates. **Load older** raises the maximum by another 300 and refreshes the data. Selecting a commit loads `git show` output and the list of paths changed by that commit.

- **Check out commit** uses `git switch --detach <hash>` and leaves HEAD detached.
- **Revert commit** runs `git revert --no-edit <hash>`, which creates a new commit reversing the selected commit. Git can stop with an error if the revert conflicts.
- **Restore selected file** runs `git restore --source=<hash> --worktree -- <path>` and replaces the working-tree copy of that file with the historical version. It does not stage the restored file.

These operations prompt for confirmation. Normal Git protections and conflict behavior still apply.

### Branches and remotes view

The branch list includes local and remote refs, current branch marker, and upstream name.

- **Create** runs `git branch <name>`; it creates but does not switch to the branch.
- **Switch** runs `git switch <branch>` for local branches. Selecting a remote branch attempts to create a tracking local branch with `git switch --track -c <local-name> <remote-name>`.
- **Delete** uses `git branch -d` for local branches (Git refuses to delete unmerged branches). For remote branches the UI runs `git push <remote> --delete <branch>`. The current branch cannot be deleted by this UI action; deletion is confirmed.
- Remotes are added with `git remote add`, changed with `git remote set-url`, and removed with `git remote remove`. Removing a remote is confirmed.
- **Clone** accepts a URL and a new or empty destination directory, then runs `git clone <url> <destination>`. A non-empty existing folder is rejected.

### Git command view and authentication

Enter a Git subcommand, for example `status --short`, and choose **Run** to capture stdout/stderr and show the exit result in the status area. Pressing Enter in the command input also runs it. **Run in terminal** starts the same parsed command in a separate interactive process.

The command input accepts single- or double-quoted arguments, for example `log --author "Toni Fey"`. It is parsed by `TokenizeCommandLine`; it is not passed through a shell and does not expand variables, pipes, redirections, or shell syntax. A missing closing quote is reported as an input error.

Background commands close standard input and set `GIT_TERMINAL_PROMPT=0`; GUI commands cannot answer terminal credential prompts. Configure Git credentials before using fetch/pull/push/clone in background mode, or use **Run in terminal** for interactive commands.

After successful button actions and captured **Run** commands the UI refreshes repository state. A command launched with **Run in terminal** is not monitored after startup; use **Refresh** when it finishes. Captured process output is kept in the Git command output area, while status messages summarize the latest action.

## Themes and layout

The header toggle switches between the light and dark resource palettes. The editor receives `IsDarkTheme`, and the Git tab recreates its diff colors to match. The design uses a restrained blue accent, compact labels, resizable panes, and a collapsible tree. Theme state is in-memory and resets to light at startup.

Initial window size is 1320 × 820 with a minimum of 1120 × 700. The explorer begins at 230 logical pixels wide, with splitters between the explorer, editor, and preview.

## Errors and troubleshooting

| Symptom | Likely cause and response |
| --- | --- |
| Preview says the Typst compiler was not found | Install Typst or add the folder containing `typst.exe` to `PATH`; restart Typify so it inherits the updated environment. |
| Imported file cannot be found | Confirm the import is relative to the main `.typ` file's folder and the imported file is within `_workingDirectory` (the Typst root). |
| Preview stays unchanged after changing an imported file | The app watches editor text, not dependency files. Make a small edit to the main editor buffer to trigger compilation. |
| PDF differs from the visible editor text | PDF export compiles a selected source file from disk. Save the desired buffer contents externally and select that file. |
| Git tab says “Not a Git repository” | Select the repository folder, initialize it, or clone a repository. If the chosen folder is nested in a repository, the tab resolves to the repository root. |
| Network Git action fails without asking for a password | Background mode suppresses prompts. Configure Git Credential Manager/SSH or rerun via **Run in terminal**. |
| Folder is missing from the explorer | Refresh the root or expand its parent. Folder contents are loaded lazily. Access permissions may also prevent enumeration. |

## Current limitations

- No editor save, save-as, dirty-buffer indicator, or multi-document tab support.
- PDF export is a separate source-file workflow and does not consume unsaved editor text.
- Preview is full Typst CLI compilation to PNG at fixed 72 PPI after a 50 ms debounce. It does not observe external dependency changes or provide in-process incremental rendering.
- Explorer has no filtering/search, file opening for non-`.typ` types, or `.gitignore` exclusion.
- Theme and repository selection are not persisted.
- Git UI does not expose every Git workflow as a dedicated button. Arbitrary commands are available through the Git command field, subject to the Git CLI installed on the machine.
