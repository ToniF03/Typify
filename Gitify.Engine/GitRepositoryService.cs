using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gitify.Engine
{
    public sealed class GitCommandResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool Succeeded { get { return ExitCode == 0; } }

        public string CombinedOutput
        {
            get
            {
                return string.Join(Environment.NewLine, new[] { StandardOutput, StandardError }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }
    }

    public sealed class GitFileChange
    {
        public string Path { get; set; }
        public string OriginalPath { get; set; }
        public char IndexStatus { get; set; }
        public char WorkTreeStatus { get; set; }
        public bool IsUntracked { get; set; }
        public string DisplayPath
        {
            get { return string.IsNullOrEmpty(OriginalPath) ? Path : Path + "  ←  " + OriginalPath; }
        }
        public string Status
        {
            get
            {
                if (IsUntracked) return "Untracked";
                string value = new string(new[] { IndexStatus, WorkTreeStatus }).Trim();
                return string.IsNullOrEmpty(value) ? "Modified" : value;
            }
        }
    }

    public sealed class GitCommit
    {
        public string Hash { get; set; }
        public string ShortHash { get; set; }
        public string Author { get; set; }
        public string Date { get; set; }
        public string Subject { get; set; }
        public string Display
        {
            get { return ShortHash + "  " + Subject; }
        }
    }

    public sealed class GitBranch
    {
        public string Name { get; set; }
        public bool IsCurrent { get; set; }
        public string Upstream { get; set; }
        public bool IsRemote { get; set; }
        public string Display { get { return (IsCurrent ? "●  " : "○  ") + Name; } }
    }

    public sealed class GitRepositoryService
    {
        private readonly string _repositoryPath;

        public GitRepositoryService(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath)) throw new ArgumentException("A repository path is required.", "repositoryPath");
            _repositoryPath = Path.GetFullPath(repositoryPath);
        }

        public string RepositoryPath { get { return _repositoryPath; } }

        public Task<GitCommandResult> RunAsync(params string[] arguments)
        {
            return RunAsync((IEnumerable<string>)arguments);
        }

        public async Task<GitCommandResult> RunAsync(IEnumerable<string> arguments)
        {
            string argumentString = string.Join(" ", arguments.Select(QuoteArgument));
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = argumentString,
                WorkingDirectory = _repositoryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start()) throw new InvalidOperationException("Git could not be started. Make sure git.exe is installed and available on PATH.");
                process.StandardInput.Close();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());
                return new GitCommandResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = await outputTask,
                    StandardError = await errorTask
                };
            }
        }

        public void StartInteractive(IEnumerable<string> arguments)
        {
            string argumentString = string.Join(" ", arguments.Select(QuoteArgument));
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = argumentString,
                WorkingDirectory = _repositoryPath,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start()) throw new InvalidOperationException("Git could not be started. Make sure git.exe is installed and available on PATH.");
            }
        }

        public static string QuoteArgument(string argument)
        {
            argument = argument ?? string.Empty;
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

        public static IList<string> TokenizeCommandLine(string commandLine)
        {
            var arguments = new List<string>();
            var token = new StringBuilder();
            char quote = '\0';
            bool tokenStarted = false;
            for (int index = 0; index < (commandLine ?? string.Empty).Length; index++)
            {
                char character = commandLine[index];
                if (character == '\\' && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                {
                    token.Append('"');
                    index++;
                    tokenStarted = true;
                    continue;
                }
                if ((character == '"' || character == '\'') && (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                    tokenStarted = true;
                    continue;
                }
                if (char.IsWhiteSpace(character) && quote == '\0')
                {
                    if (tokenStarted)
                    {
                        arguments.Add(token.ToString());
                        token.Clear();
                        tokenStarted = false;
                    }
                    continue;
                }
                token.Append(character);
                tokenStarted = true;
            }

            if (quote != '\0') throw new FormatException("A quoted argument is missing its closing quote.");
            if (tokenStarted) arguments.Add(token.ToString());
            return arguments;
        }

        public static IList<GitFileChange> ParseStatus(string output)
        {
            var changes = new List<GitFileChange>();
            string[] fields = (output ?? string.Empty).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < fields.Length; index++)
            {
                string record = fields[index];
                if (record.StartsWith("## ", StringComparison.Ordinal))
                {
                    int headerEnd = record.IndexOf('\n');
                    if (headerEnd < 0) continue;
                    record = record.Substring(headerEnd + 1);
                }
                if (record.Length < 4) continue;
                char x = record[0];
                char y = record[1];
                var change = new GitFileChange
                {
                    IndexStatus = x,
                    WorkTreeStatus = y,
                    IsUntracked = x == '?' && y == '?',
                    Path = record.Substring(3)
                };
                if ((x == 'R' || x == 'C' || y == 'R' || y == 'C') && index + 1 < fields.Length)
                    change.OriginalPath = fields[++index];
                changes.Add(change);
            }
            return changes;
        }

        public static IList<GitCommit> ParseLog(string output)
        {
            var commits = new List<GitCommit>();
            string[] fields = (output ?? string.Empty).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index + 4 < fields.Length; index += 5)
            {
                commits.Add(new GitCommit
                {
                    Hash = fields[index],
                    ShortHash = fields[index + 1],
                    Author = fields[index + 2],
                    Date = fields[index + 3],
                    Subject = fields[index + 4]
                });
            }
            return commits;
        }
    }
}
