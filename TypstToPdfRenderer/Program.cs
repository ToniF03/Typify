using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace TypstToPdfRenderer
{
    internal static class Program
    {
        private const string DefaultCompiler = "typst";

        private static int Main(string[] args)
        {
            string compiler = DefaultCompiler;
            var compilerArguments = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--typst-path", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Fehler: Nach --typst-path muss der Pfad zu typst.exe folgen.");
                        return 2;
                    }
                    compiler = args[++i];
                    continue;
                }

                if (i == 0 && (args[i] == "--help" || args[i] == "-h" || args[i] == "/?"))
                {
                    PrintUsage();
                    return 0;
                }

                if (i == 0 && args[i] == "--version")
                {
                    Console.WriteLine("TypstToPdfRenderer 1.0");
                    return RunCompiler(compiler, new[] { "--version" });
                }

                compilerArguments.Add(args[i]);
            }

            if (compilerArguments.Count == 0)
            {
                PrintUsage();
                return 2;
            }

            return RunCompiler(compiler, BuildCompileArguments(compilerArguments));
        }

        private static IEnumerable<string> BuildCompileArguments(List<string> arguments)
        {
            var result = new List<string> { "compile", "--format", "pdf" };
            result.AddRange(arguments);
            return result;
        }

        private static int RunCompiler(string compiler, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = compiler,
                Arguments = JoinArguments(arguments),
                UseShellExecute = false,
                CreateNoWindow = Console.IsOutputRedirected || Console.IsErrorRedirected,
                WorkingDirectory = Environment.CurrentDirectory
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Console.Error.WriteLine("Typst konnte nicht gestartet werden.");
                        return 1;
                    }
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception)
            {
                Console.Error.WriteLine("Der Typst-Compiler wurde nicht gefunden. Installiere Typst oder gib den Pfad mit --typst-path an.");
                return 127;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Typst konnte nicht gestartet werden: " + exception.Message);
                return 1;
            }
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            var quoted = new List<string>();
            foreach (string argument in arguments)
                quoted.Add(QuoteArgument(argument));
            return string.Join(" ", quoted);
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return argument;

            var result = new System.Text.StringBuilder();
            result.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    result.Append('\\', backslashes);
                    backslashes = 0;
                }
                result.Append(character);
            }
            if (backslashes > 0) result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TypstToPdfRenderer — Typst nach PDF konvertieren");
            Console.WriteLine();
            Console.WriteLine("Aufruf:");
            Console.WriteLine("  TypstToPdfRenderer [--typst-path <typst.exe>] <eingabe.typ> [ausgabe.pdf] [Typst-Optionen]");
            Console.WriteLine();
            Console.WriteLine("Beispiele:");
            Console.WriteLine("  TypstToPdfRenderer dokument.typ");
            Console.WriteLine("  TypstToPdfRenderer dokument.typ ausgabe.pdf --pdf-standard 2.0");
            Console.WriteLine("  TypstToPdfRenderer --typst-path C:\\Tools\\typst.exe dokument.typ");
            Console.WriteLine();
            Console.WriteLine("Typst-Optionen wie --root, --input, --font-path und --pages werden an den Compiler weitergegeben.");
            Console.WriteLine("PDF-Ausgabe und unterstützte Optionen: https://typst.app/docs/reference/pdf/");
        }
    }
}
