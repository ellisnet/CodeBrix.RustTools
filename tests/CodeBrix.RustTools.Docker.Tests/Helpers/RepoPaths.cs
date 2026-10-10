using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// Locates the repository and its rust-samples folder. The repository root is found, in
/// order: from the CODEBRIX_RUSTTOOLS_REPO_ROOT environment variable; by walking up from this
/// helper's own source file (recorded at compile time); by walking up from the test output
/// folder. The second route keeps working when the tests are built with --artifacts-path.
/// (A copy of the helper of the same name in CodeBrix.RustTools.Tests.)
/// </summary>
internal static class RepoPaths
{
    public const string RepoRootVariable = "CODEBRIX_RUSTTOOLS_REPO_ROOT";

    public const string SolutionFileName = "CodeBrix.RustTools.slnx";

    private static readonly Lazy<string> Root = new Lazy<string>(FindRoot);

    public static string RepoRoot => Root.Value;

    public static string RustSamplesDirectory => Path.Combine(RepoRoot, "rust-samples");

    public static string SampleName(int number) => $"test_project_{number:00}";

    public static string Sample(int number) => Path.Combine(RustSamplesDirectory, SampleName(number));

    /// <summary>The full path of this source file, as recorded by the compiler.</summary>
    public static string SourceFilePath => ThisFilePath();

    /// <summary>The repository root named by the environment variable, or <c>null</c> when it
    /// is unset or does not hold the solution file.</summary>
    public static string FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(RepoRootVariable);
        return !string.IsNullOrWhiteSpace(value) && IsRepoRoot(value.Trim()) ? Path.GetFullPath(value.Trim()) : null;
    }

    /// <summary>The repository root found by walking up from this source file, or <c>null</c>
    /// (for example when the binaries were built on another machine).</summary>
    public static string FromCallerFilePath() => WalkUp(Path.GetDirectoryName(SourceFilePath));

    /// <summary>The repository root found by walking up from the test output folder, or
    /// <c>null</c>.</summary>
    public static string FromBaseDirectory() => WalkUp(AppContext.BaseDirectory);

    /// <summary>The first folder at or above <paramref name="start"/> that holds the solution
    /// file, or <c>null</c>.</summary>
    public static string WalkUp(string start)
    {
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            return null;
        }

        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (IsRepoRoot(directory.FullName))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    private static bool IsRepoRoot(string directory) => File.Exists(Path.Combine(directory, SolutionFileName));

    private static string FindRoot()
        => FromEnvironment()
            ?? FromCallerFilePath()
            ?? FromBaseDirectory()
            ?? throw new InvalidOperationException(
                $"{SolutionFileName} was not found. Set {RepoRootVariable} to the repository root; searched above "
                + SourceFilePath + " and " + AppContext.BaseDirectory);
}
