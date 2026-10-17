# Gitify.Engine library manual

`Gitify.Engine` is a UI-independent .NET library for invoking Git in a chosen working directory and parsing the output formats needed by Typify. It is a pure class library: it contains no window, WPF control, or application entry point. It delegates repository behavior to the installed `git` executable instead of implementing Git's storage, merge algorithms, credential protocols, or network transport.

## Contents

- [Requirements and build](#requirements-and-build)
- [Architecture](#architecture)
- [Quick start](#quick-start)
- [Public API reference](#public-api-reference)
- [Output parser formats](#output-parser-formats)
- [Process behavior and errors](#process-behavior-and-errors)
- [Security and operational guidance](#security-and-operational-guidance)
- [Library scope](#library-scope)

## Requirements and build

- .NET Framework 4.7.2.
- Git installed and available as `git` on `PATH`.
- Access rights for the selected repository and any files or remotes involved in the command.

Build `Gitify.Engine/Gitify.Engine.csproj` with Visual Studio/MSBuild and the .NET Framework 4.7.2 developer pack. It produces `Gitify.Engine.dll` and references only `System` and `System.Core`. `Typify/Typify.csproj` has a project reference to it.

## Architecture

The project currently consists of one source file, `Gitify.Engine/GitRepositoryService.cs`:

- `GitRepositoryService` owns an absolute repository path and starts Git child processes.
- `GitCommandResult` returns exit status and output streams.
- `GitFileChange`, `GitCommit`, and `GitBranch` are plain data models for common repository views.
- Static parser/tokenizer helpers transform Git's stable/customized output or the Git command input field.

The library provides no persistence layer, repository cache, event stream, automatic file watcher, retry policy, UI dispatcher, or command queue. The caller decides when to refresh repository state.

## Quick start

```csharp
using Gitify.Engine;

var repository = new GitRepositoryService(@"C:\Projects\TypstProject");
GitCommandResult result = await repository.RunAsync("status", "--short");

if (!result.Succeeded)
    throw new InvalidOperationException(result.CombinedOutput);

Console.WriteLine(result.StandardOutput);
```

The repository path is converted to a full path by the constructor and is exposed through the read-only `RepositoryPath` property. The constructor rejects null, empty, or whitespace-only paths. It does not check that the directory exists or that it is a Git repository; the command result or process error reports those conditions later.

## Public API reference

### `GitRepositoryService`

```csharp
public GitRepositoryService(string repositoryPath)
public string RepositoryPath { get; }
public Task<GitCommandResult> RunAsync(params string[] arguments)
public Task<GitCommandResult> RunAsync(IEnumerable<string> arguments)
public void StartInteractive(IEnumerable<string> arguments)
public static string QuoteArgument(string argument)
public static IList<string> TokenizeCommandLine(string commandLine)
public static IList<GitFileChange> ParseStatus(string output)
public static IList<GitCommit> ParseLog(string output)
```

`RunAsync` accepts the Git subcommand and each argument as a separate value. Do not pass a single shell command string to the `params` overload unless it is intended to be one Git argument. Example:

```csharp
GitCommandResult diff = await repository.RunAsync("diff", "--", "report.typ");
GitCommandResult log = await repository.RunAsync("log", "--oneline", "-10");
```

`StartInteractive` starts a terminal-attached Git process with the repository as its working directory. It returns after starting the process; it does not expose the process handle, capture output, or wait for completion.

`QuoteArgument` implements Windows process-argument quoting for a single value. `TokenizeCommandLine` parses a user-entered command into arguments using whitespace and single/double quotes. It is a tokenizer, not a shell: it does not expand variables, glob patterns, pipes, redirections, command substitutions, or environment variables.

### `GitCommandResult`

| Property | Type | Description |
| --- | --- | --- |
| `ExitCode` | `int` | Exit code returned by Git. |
| `StandardOutput` | `string` | Captured stdout, decoded as UTF-8. |
| `StandardError` | `string` | Captured stderr, decoded as UTF-8. |
| `Succeeded` | `bool` | `true` when `ExitCode == 0`. |
| `CombinedOutput` | `string` | Non-empty stdout and stderr joined with the platform newline. |

Always check `Succeeded` before treating an operation as complete. Git commonly writes progress and errors to stderr; stderr content alone does not determine success.

### `GitFileChange`

| Property | Type | Description |
| --- | --- | --- |
| `Path` | `string` | Current or destination path reported by Git. |
| `OriginalPath` | `string` | Source path for parsed rename/copy records; otherwise null. |
| `IndexStatus` | `char` | Porcelain status in the index column. |
| `WorkTreeStatus` | `char` | Porcelain status in the working-tree column. |
| `IsUntracked` | `bool` | True when both porcelain columns are `?`. |
| `DisplayPath` | `string` | Current path, or current path with original path for rename/copy. |
| `Status` | `string` | `Untracked` for unknown files; otherwise trimmed status columns or `Modified` if empty. |

For regular status entries, the two status characters preserve staged and unstaged state separately. Their meaning follows Git's porcelain status format, not a Gitify-specific enum.

### `GitCommit`

Contains `Hash`, `ShortHash`, `Author`, `Date`, and `Subject` strings. `Display` produces `<short hash>  <subject>` for a simple list row.

### `GitBranch`

Contains `Name`, `IsCurrent`, `Upstream`, and `IsRemote`. `Display` prefixes current branches with a filled-circle marker and other branches with an open-circle marker. The model itself does not execute branch operations.

## Output parser formats

### Status parser

`ParseStatus` expects NUL-delimited output equivalent to:

```text
git status --porcelain=v1 -z --branch
```

The branch header is ignored after its first line. Each status record supplies the two index/work-tree status characters and path. For rename/copy status, the following NUL field is consumed as `OriginalPath`. Untracked records are marked when both columns are `?`.

Example:

```csharp
GitCommandResult status = await repository.RunAsync(
    "status", "--porcelain=v1", "-z", "--branch");
IList<GitFileChange> changes = GitRepositoryService.ParseStatus(status.StandardOutput);
```

### Log parser

`ParseLog` expects groups of five non-empty NUL-separated fields in this order: full hash, short hash, author, date, subject. The Typify Git tab produces that format with:

```text
git log --all --date=iso-strict --format=%H%x00%h%x00%an%x00%ad%x00%s%x00
```

This is not a parser for arbitrary `git log` formatting. Use an explicit format that matches the field order above.

### Branches

There is no public `ParseBranches` helper. Typify's Git view runs `git branch --all --format=%(refname)%09%(HEAD)%09%(upstream:short)` and parses each tab-separated line in the UI layer.

## Process behavior and errors

### `RunAsync`

- Calls `git` directly with `UseShellExecute = false`; no command shell is involved.
- Uses `RepositoryPath` as `WorkingDirectory`.
- Quotes the provided argument array with `QuoteArgument` and joins it into the process arguments string.
- Redirects stdin, stdout, and stderr; immediately closes stdin; reads both output streams asynchronously as UTF-8.
- Sets `GIT_TERMINAL_PROMPT=0`, creates no visible window, and waits for process completion.
- Returns a `GitCommandResult` for both successful and unsuccessful exit codes.

If Git cannot be started, `Process.Start` can throw (for example, because the executable is missing) or return false. A false return becomes an `InvalidOperationException` explaining the PATH requirement. Invalid working directories and other process startup errors may also throw. Callers should handle both exceptions and non-zero result codes.

There is no cancellation token or timeout parameter. A Git command that waits indefinitely for network or process activity will keep its task pending until Git exits.

### `StartInteractive`

Starts `git` with redirected streams disabled and window creation enabled. It is suitable for commands that require terminal interaction. The caller cannot observe its final exit status using this method.

## Security and operational guidance

- Pass arguments as separate strings rather than concatenating user input into one shell command. Gitify does not use a shell, but callers remain responsible for choosing allowed operations and validating application-level inputs.
- Git commands can modify or delete data. Confirm destructive operations at the UI/business-logic layer before calling the engine.
- Do not assume that a zero/non-zero exit code is the only useful signal; preserve and inspect both output streams for diagnostics.
- Configure credentials in Git. `RunAsync` cannot answer terminal prompts. Use an OS credential manager, SSH configuration, or `StartInteractive` where interactive authentication is required.
- If a command's behavior depends on a repository state change, run a follow-up status/log/branch query; the engine does not maintain cached state.

## Library scope

The engine supports arbitrary Git subcommands through `RunAsync`, so the library does not need a method for each operation. The Typify UI provides convenience actions for status/diff, stage/unstage, discard, commit, fetch/pull/push, history, checkout/revert/restore, branch and remote management, clone, plus a generic Git command field. Advanced commands such as stash, cherry-pick, rebase, tag, bisect, and worktree are available through the generic command interface or a separate Git terminal, provided the installed Git version supports them.

Gitify.Engine is not a complete Git GUI SDK: it has no repository initialization abstraction, typed command model, conflict editor, merge/rebase UI, credential UI, progress events, cancellation, or general-purpose parser for arbitrary Git output formats.
