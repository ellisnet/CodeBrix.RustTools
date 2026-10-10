using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// Compares the installed Rust tools with the latest stable release and the latest rustup
/// release, and carries the update guidance to show when something is out of date.
/// </summary>
public sealed class ToolUpdateReport
{
    /// <summary>One comparison per checked tool (rustc, cargo, rustup, clippy, rustfmt).</summary>
    public IReadOnlyList<ToolUpdateCheck> Checks { get; init; } = Array.Empty<ToolUpdateCheck>();

    /// <summary>The stable release that was compared against, or <c>null</c> when the channel
    /// manifest could not be read (see <see cref="Errors"/>).</summary>
    public RustReleaseInfo LatestRelease { get; init; }

    /// <summary>The latest rustup version, or <c>null</c> when the rustup manifest could not be
    /// read.</summary>
    public ToolVersion LatestRustupVersion { get; init; }

    /// <summary>Update guidance for the outdated tools; empty when everything is current.</summary>
    public IReadOnlyList<InstallGuidance> Guidance { get; init; } = Array.Empty<InstallGuidance>();

    /// <summary>Problems met while fetching the release information (the report is still usable
    /// for the parts that succeeded).</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>When the check ran.</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True when at least one tool has a newer release.</summary>
    public bool AnyUpdateAvailable => Checks.Any(c => c.IsUpdateAvailable);

    /// <summary>The checks whose tool has a newer release.</summary>
    public IReadOnlyList<ToolUpdateCheck> OutdatedTools => Checks.Where(c => c.IsUpdateAvailable).ToList();

    /// <summary>
    /// Returns the comparison for one tool, or <c>null</c> when it was not checked.
    /// </summary>
    /// <param name="tool">The tool to look up.</param>
    public ToolUpdateCheck GetCheck(RustTool tool) => Checks.FirstOrDefault(c => c.Tool == tool);

    /// <summary>
    /// Builds a report from a detected toolchain and the release information (no network
    /// access). Either release argument may be <c>null</c> when it could not be fetched.
    /// </summary>
    /// <param name="toolchain">The detected environment.</param>
    /// <param name="latestRelease">The latest stable channel release, or <c>null</c>.</param>
    /// <param name="latestRustup">The latest rustup version, or <c>null</c>.</param>
    /// <param name="errors">Fetch problems to record, or <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="toolchain"/> is null.</exception>
    public static ToolUpdateReport Create(RustToolchainInfo toolchain, RustReleaseInfo latestRelease, ToolVersion latestRustup, IEnumerable<string> errors = null)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        var checks = new List<ToolUpdateCheck>
        {
            Compare(toolchain.Rustc, latestRelease?.RustcVersion),
            Compare(toolchain.Cargo, latestRelease?.CargoVersion),
            Compare(toolchain.Rustup, latestRustup),
            Compare(toolchain.Clippy, latestRelease?.ClippyVersion),
            Compare(toolchain.Rustfmt, latestRelease?.RustfmtVersion),
        };

        var guidance = checks.Any(c => c.IsUpdateAvailable)
            ? new[] { InstallGuidanceProvider.ForUpdate(toolchain.OperatingSystem, toolchain.IsRustupManaged) }
            : Array.Empty<InstallGuidance>();

        return new ToolUpdateReport
        {
            Checks = checks,
            LatestRelease = latestRelease,
            LatestRustupVersion = latestRustup,
            Guidance = guidance,
            Errors = errors?.Where(e => !string.IsNullOrEmpty(e)).ToList() ?? new List<string>(),
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// A multi-line summary with one line per tool.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder(AnyUpdateAvailable ? "Updates are available." : "No updates found.");
        foreach (var check in Checks)
        {
            builder.AppendLine().Append("  ").Append(check);
        }

        foreach (var error in Errors)
        {
            builder.AppendLine().Append("  Error: ").Append(error);
        }

        return builder.ToString();
    }

    private static ToolUpdateCheck Compare(InstalledTool installed, ToolVersion latest)
    {
        ToolStatus status;
        if (installed.Status == ToolStatus.Missing)
        {
            status = ToolStatus.Missing;
        }
        else if (installed.Version == null || latest == null)
        {
            status = ToolStatus.Unknown;
        }
        else
        {
            status = installed.Version < latest ? ToolStatus.Outdated : ToolStatus.Installed;
        }

        return new ToolUpdateCheck
        {
            Tool = installed.Tool,
            Installed = installed.Version,
            Latest = latest,
            Status = status,
        };
    }
}
