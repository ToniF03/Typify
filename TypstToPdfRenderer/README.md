# TypstToPdfRenderer manual

`TypstToPdfRenderer` is a standalone console executable that turns a Typst source file into PDF by invoking the installed Typst CLI. It delegates parsing, layout, fonts, imports, diagnostics, and PDF standards to Typst itself; the wrapper does not implement the Typst language or render documents in-process.

## Requirements and build

- Windows/.NET Framework 4.7.2 for the built executable in this solution.
- The Typst CLI, either available as `typst` on `PATH` or provided with `--typst-path`.

Build `TypstToPdfRenderer/TypstToPdfRenderer.csproj` with Visual Studio/MSBuild and the .NET Framework 4.7.2 developer pack. The project is independent of `Typify`, `TypifyTextbox`, and `Gitify.Engine` and references only framework assemblies.

## Command line

```text
TypstToPdfRenderer [--typst-path <typst-executable>] <input.typ> [output.pdf] [Typst compile options]
TypstToPdfRenderer --help
TypstToPdfRenderer --version
```

The wrapper constructs the Typst argument list by prepending `compile --format pdf` and forwarding the remaining arguments:

```text
typst compile --format pdf <input.typ> [output.pdf] [Typst compile options]
```

If `output.pdf` is omitted, Typst chooses its normal output path based on the input file. The wrapper does not calculate or rewrite the destination itself.

### Examples

```powershell
# Use typst.exe from PATH. Output defaults to report.pdf next to report.typ.
TypstToPdfRenderer.exe .\report.typ

# Specify the output path.
TypstToPdfRenderer.exe .\report.typ .\build\report.pdf

# Pass options through to Typst's compile command.
TypstToPdfRenderer.exe .\report.typ .\report.pdf --pdf-standard 2.0

# Use an explicit compiler path when Typst is not on PATH.
TypstToPdfRenderer.exe --typst-path "C:\Tools\typst.exe" .\report.typ .\report.pdf

# Display the compiler version through the wrapper.
TypstToPdfRenderer.exe --version
```

Use quotes around paths containing spaces. The wrapper quotes each forwarded process argument for Windows process invocation. It does not invoke a shell, so shell variables, pipes, redirections, and wildcard expansion are not processed by the wrapper.

## Wrapper options

### `--typst-path <path>`

Select the Typst executable. The option is consumed by the wrapper and is not passed to Typst. If omitted, the executable name `typst` is resolved through the process `PATH`. The option may appear among the wrapper arguments; place it before the source path for clarity.

### `--help`, `-h`, `/?`

Prints wrapper usage and exits with status 0 when supplied as the first argument.

### `--version`

When supplied as the first argument, prints wrapper version `1.0` and runs the selected Typst executable with `--version`; the wrapper returns the compiler process exit code.

### Typst compile options

Arguments not consumed by the wrapper are forwarded unchanged to `typst compile`. The wrapper does not validate option names. Common examples include root/project configuration, font paths, input values, page selection, PDF standard, and output path options. Consult the current [Typst CLI documentation](https://typst.app/docs/reference/cli/) and [PDF reference](https://typst.app/docs/reference/pdf/) for accepted compiler arguments.

## Working directory and paths

Typst is launched with the calling process's current directory as its working directory (`Environment.CurrentDirectory`). Relative input, output, import, font, and root paths are interpreted by Typst from that context according to Typst's own command-line rules. The wrapper does not change to the input file's directory; choose an appropriate current directory or pass absolute paths.

`ProcessStartInfo.UseShellExecute` is disabled. The wrapper waits for the Typst process to exit and returns its exit code. Child output is not captured in memory; it is inherited by the wrapper's console. A console window is not created when wrapper stdout or stderr is redirected.

## Exit status and errors

- `0`: help succeeded, or Typst completed successfully.
- `2`: no compile arguments were supplied, or `--typst-path` has no following value.
- `127`: the compiler could not be started because it was not found/launchable as a Windows executable.
- `1`: other wrapper-level process startup error; or the Typst process may itself return `1` for a compiler error.
- Any other compiler exit code is passed through unchanged.

If Typst is missing, install it or supply `--typst-path`. If compilation fails, use Typst's diagnostic output to resolve the source/import/font issue; the wrapper does not parse or translate compiler diagnostics.

## Current implementation notes

- The executable is named `TypstToPdfRenderer.exe` by the project. The repository root readme links here from its solution overview.
- This console wrapper is separate from the Typify WPF app. Typify has its own live PNG preview and PDF export process calls; it does not start this wrapper executable.
- Current CLI usage and launch-error strings emitted by `Program.cs` are German, while this reference and the Typify application UI are English.

## Source reference

- [Typst documentation](https://typst.app/docs/)
- [Typst CLI documentation](https://typst.app/docs/reference/cli/)
- [Typst PDF reference](https://typst.app/docs/reference/pdf/)
