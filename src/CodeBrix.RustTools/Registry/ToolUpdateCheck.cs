using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// The installed-versus-latest comparison for one tool.
/// </summary>
public sealed class ToolUpdateCheck
{
    /// <summary>The tool compared.</summary>
    public RustTool Tool { get; init; }

    /// <summary>The installed version, or <c>null</c> when the tool is missing or its version is
    /// unknown.</summary>
    public ToolVersion Installed { get; init; }

    /// <summary>The latest released version, or <c>null</c> when it could not be determined.</summary>
    public ToolVersion Latest { get; init; }

    /// <summary><see cref="ToolStatus.Outdated"/> when <see cref="Installed"/> is older than
    /// <see cref="Latest"/>, <see cref="ToolStatus.Installed"/> when current (or newer, as with a
    /// nightly), <see cref="ToolStatus.Missing"/> when the tool is not installed, and
    /// <see cref="ToolStatus.Unknown"/> when either version is unknown.</summary>
    public ToolStatus Status { get; init; }

    /// <summary>True when <see cref="Status"/> is <see cref="ToolStatus.Outdated"/>.</summary>
    public bool IsUpdateAvailable => Status == ToolStatus.Outdated;

    /// <summary>
    /// A one-line description such as <c>Rustc: 1.94.1 -> 1.99.0 (Outdated)</c>.
    /// </summary>
    public override string ToString()
        => $"{Tool}: {Installed?.ToString() ?? "?"} -> {Latest?.ToString() ?? "?"} ({Status})";
}
