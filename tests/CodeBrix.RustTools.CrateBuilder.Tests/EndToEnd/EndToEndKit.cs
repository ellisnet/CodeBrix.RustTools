using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using CodeBrix.RustTools.Docker;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// Shared pieces of the end-to-end tests: one scratch root under the test output (so the API
/// extractor tool is built once and reused between tests and runs), builders, a timing log
/// (e2e-log.txt in the test output folder) and consumer apps built and run with the dotnet CLI.
/// Every crate, output and app folder lives in a sandbox under the test output; cargo never runs
/// inside rust-samples/.
/// </summary>
internal static class EndToEndKit
{
    public static readonly TimeSpan DotnetTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The scratch root the end-to-end builds share (kept between runs; each build's
    /// run folder inside it is deleted unless KeepScratch).</summary>
    public static string ScratchRoot => Path.Combine(AppContext.BaseDirectory, "cratebuilder-scratch");

    /// <summary>A builder with the shared scratch root (and, for Docker tests, the given
    /// isolated workbench options).</summary>
    public static CrateBuilder NewBuilder(DockerRustWorkbenchOptions dockerOptions = null, bool keepScratch = false)
        => new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = ScratchRoot, DockerOptions = dockerOptions, KeepScratch = keepScratch });

    /// <summary>The host's own CrateTarget, or a skip when this host is not one of the nine.</summary>
    public static CrateTarget HostTargetOrSkip()
    {
        var host = WebGpu.WebGpuTestPackage.HostTarget();
        if (host == null)
        {
            Assert.Skip("This host is not one of the runtime identifiers CrateBuilder builds for.");
        }

        return host.Value;
    }

    /// <summary>Appends a line with the test's duration to e2e-log.txt in the test output
    /// folder.</summary>
    public static void Log(string name, Stopwatch stopwatch, string note = null)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "e2e-log.txt"), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {name}: {stopwatch.Elapsed.TotalSeconds:F1} s{(note == null ? string.Empty : " (" + note + ")")}\n");
        }
        catch (IOException)
        {
            // The log is informational.
        }
    }

    /// <summary>Runs dotnet and asserts exit code 0; returns the output.</summary>
    public static async Task<string> DotnetAsync(string directory, params string[] arguments)
    {
        var (exitCode, output) = await DotnetCli.RunAsync(directory, DotnetTimeout, arguments);
        exitCode.Should().Be(0, "dotnet " + string.Join(" ", arguments) + " failed in " + directory + ":\n" + output);
        return output;
    }

    /// <summary>"build -warnaserror" with the self-contained build flags.</summary>
    public static string[] Build(params string[] extra) => new[] { "build", "-warnaserror" }.Concat(DotnetCli.BuildFlags()).Concat(extra).ToArray();

    /// <summary>Writes a console app referencing <paramref name="wrapperProject"/> with
    /// <paramref name="program"/> as Program.cs, builds it with warnings as errors, runs it and
    /// returns its output.</summary>
    public static async Task<string> RunConsumerAppAsync(string directory, string wrapperProject, string program, bool allowUnsafe = false)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>disable</Nullable>\n    <ImplicitUsings>disable</ImplicitUsings>\n"
            + (allowUnsafe ? "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n" : string.Empty)
            + "  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"" + wrapperProject + "\" />\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(directory, "Program.cs"), program);
        await DotnetAsync(directory, Build());
        return await DotnetAsync(directory, "run", "--no-build");
    }

    /// <summary>The lines of a program's output, trimmed.</summary>
    public static IReadOnlyList<string> Lines(string output)
        => output.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}
