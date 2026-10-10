using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The outcome of <see cref="RustWorkbench.BuildAndroidAsync"/>: one cargo result per target
/// and the files copied to the output folder.
/// </summary>
public sealed class AndroidBuildResult
{
    /// <summary>The cargo results, keyed by target triple, in build order.</summary>
    public IReadOnlyDictionary<string, CargoCommandResult> Builds { get; init; } = new Dictionary<string, CargoCommandResult>();

    /// <summary>The folder the files were written to.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>The layout the files follow.</summary>
    public AndroidOutputLayout Layout { get; init; }

    /// <summary>The files copied, for every target whose build succeeded.</summary>
    public IReadOnlyList<AndroidBuildFile> Files { get; init; } = Array.Empty<AndroidBuildFile>();

    /// <summary>Whether every build succeeded and at least one file was copied.</summary>
    public bool Success => Builds.Count > 0 && Builds.Values.All(b => b.Success) && Files.Count > 0;

    /// <summary>Returns the outcome in one line.</summary>
    public override string ToString()
        => Success
            ? $"{Files.Count} file(s) in {OutputDirectory} ({Layout}): {string.Join(", ", Files.Select(f => f.Abi + "/" + f.FileName))}"
            : "Android build failed: " + string.Join(", ", Builds.Select(b => $"{b.Key} {(b.Value.Success ? "ok" : "failed")}")) + $", {Files.Count} file(s)";
}
