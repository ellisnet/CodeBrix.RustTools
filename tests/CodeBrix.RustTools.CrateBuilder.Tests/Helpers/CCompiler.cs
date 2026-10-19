using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

/// <summary>
/// The machine's C compiler for the header checks: a generated facade header must compile as
/// strict C11 (warnings as errors) and be includable twice. Skips by name without a compiler.
/// </summary>
internal static class CCompiler
{
    /// <summary>The C compiler (<c>CC</c>, else <c>cc</c> on PATH), or a skip.</summary>
    public static string FindOrSkip()
    {
        var compiler = Find();
        if (compiler == null)
        {
            Assert.Skip("No C compiler: neither CC nor cc on PATH (install the C toolchain host mode needs, e.g. Debian: sudo apt install build-essential).");
        }

        return compiler;
    }

    /// <summary>The C compiler (<c>CC</c>, else <c>cc</c> on PATH), or <c>null</c>.</summary>
    public static string Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("CC");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var name = OperatingSystem.IsWindows() ? "cc.exe" : "cc";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Compiles a translation unit that includes <paramref name="headerPath"/> twice
    /// with <c>-std=c11 -Wall -Wextra -Werror -pedantic-errors -fsyntax-only</c>; returns the exit
    /// code and the compiler's output. The unit is written into
    /// <paramref name="workDirectory"/>.</summary>
    public static async Task<(int ExitCode, string Output)> CheckHeaderAsync(string compiler, string headerPath, string workDirectory)
    {
        var header = Path.GetFileName(headerPath);
        var main = Path.Combine(workDirectory, "header-check.c");
        File.WriteAllText(main, "#include \"" + header + "\"\n#include \"" + header + "\"\n\nint main(void)\n{\n    return 0;\n}\n");
        var start = new ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-std=c11", "-Wall", "-Wextra", "-Werror", "-pedantic-errors", "-fsyntax-only", "-I", Path.GetDirectoryName(headerPath), main })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await standardOutput + await standardError);
    }
}
