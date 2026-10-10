using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The outcome of <see cref="RustWorkbench.BuildMacOSUniversalAsync"/>: the two
/// single-architecture builds and the universal files made from them.
/// </summary>
public sealed class MacOSUniversalBuildResult
{
    /// <summary>The <c>aarch64-apple-darwin</c> build.</summary>
    public CargoCommandResult Arm64Build { get; init; }

    /// <summary>The <c>x86_64-apple-darwin</c> build.</summary>
    public CargoCommandResult X64Build { get; init; }

    /// <summary>The folder the universal files were written to.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>The universal files, one per program or library that both builds produced;
    /// empty when either build failed.</summary>
    public IReadOnlyList<MacOSUniversalFile> Files { get; init; } = Array.Empty<MacOSUniversalFile>();

    /// <summary>Whether both builds succeeded and at least one universal file was written.</summary>
    public bool Success => Arm64Build != null && Arm64Build.Success && X64Build != null && X64Build.Success && Files.Count > 0;

    /// <summary>Returns the outcome in one line.</summary>
    public override string ToString()
        => Success
            ? $"{Files.Count} universal file(s) in {OutputDirectory}: {string.Join(", ", Files.Select(f => f.FileName))}"
            : $"universal build failed (arm64 {(Arm64Build?.Success == true ? "ok" : "failed")}, x86_64 {(X64Build?.Success == true ? "ok" : "failed")}, {Files.Count} file(s))";
}
