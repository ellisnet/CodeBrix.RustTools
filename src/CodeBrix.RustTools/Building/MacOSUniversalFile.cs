using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// A universal macOS file written by <see cref="RustWorkbench.BuildMacOSUniversalAsync"/>:
/// the program or library of one name, holding the arm64 and x86_64 builds.
/// </summary>
public sealed class MacOSUniversalFile
{
    /// <summary>The universal file.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The file name (the same name the single-architecture builds have).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The single-architecture files that were joined, in the order of the slices.</summary>
    public IReadOnlyList<string> SlicePaths { get; init; } = Array.Empty<string>();

    /// <summary>The slices as laid out in the file.</summary>
    public IReadOnlyList<MacOSBinarySlice> Slices { get; init; } = Array.Empty<MacOSBinarySlice>();

    /// <summary>Whether the file is a program (an executable bit was set) rather than a library.</summary>
    public bool IsExecutable { get; init; }

    /// <summary>Returns the path and the architectures.</summary>
    public override string ToString() => $"{Path} ({string.Join(" + ", Slices)})";
}
