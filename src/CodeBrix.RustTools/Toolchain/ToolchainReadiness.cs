using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The answer to "can this machine build the project?": the blocking problems and warnings
/// found by checking a <see cref="RustToolchainInfo"/> against <see cref="ToolchainRequirements"/>.
/// Pass it to <see cref="InstallGuidanceProvider.ForReadiness"/> for the instructions that fix
/// each problem.
/// </summary>
public sealed class ToolchainReadiness
{
    private ToolchainReadiness(RustToolchainInfo toolchain, ToolchainRequirements requirements, IReadOnlyList<ReadinessIssue> issues)
    {
        Toolchain = toolchain;
        Requirements = requirements;
        Issues = issues;
    }

    /// <summary>The detected environment that was evaluated.</summary>
    public RustToolchainInfo Toolchain { get; }

    /// <summary>The requirements it was evaluated against.</summary>
    public ToolchainRequirements Requirements { get; }

    /// <summary>Every problem found, blocking ones first.</summary>
    public IReadOnlyList<ReadinessIssue> Issues { get; }

    /// <summary>True when no blocking problem was found, so a build can be attempted.</summary>
    public bool IsReady => !Issues.Any(i => i.IsBlocking);

    /// <summary>The operating-system family of the evaluated environment.</summary>
    public OperatingSystemKind OperatingSystem => Toolchain.OperatingSystem;

    /// <summary>The tools reported as missing by a blocking issue.</summary>
    public IReadOnlyList<RustTool> MissingTools
        => Issues.Where(i => i.Kind == ReadinessIssueKind.ToolMissing && i.Tool.HasValue).Select(i => i.Tool.Value).Distinct().ToList();

    /// <summary>The tools older than the required minimum version.</summary>
    public IReadOnlyList<RustTool> OutdatedTools
        => Issues.Where(i => i.Kind == ReadinessIssueKind.ToolOutdated && i.Tool.HasValue).Select(i => i.Tool.Value).Distinct().ToList();

    /// <summary>The required target triples that are not installed.</summary>
    public IReadOnlyList<string> MissingTargets
        => Issues.Where(i => i.Kind == ReadinessIssueKind.TargetMissing && i.Subject != null).Select(i => i.Subject).Distinct().ToList();

    /// <summary>The Linux target triples whose GNU cross tools
    /// (<see cref="ToolchainRequirements.RequiredLinuxCrossTools"/>) were not found.</summary>
    public IReadOnlyList<string> MissingLinuxCrossTargets
        => Issues.Where(i => i.Kind == ReadinessIssueKind.LinuxCrossToolMissing && i.Subject != null).Select(i => i.Subject).Distinct().ToList();

    /// <summary>True when an Android NDK was required and none can be used.</summary>
    public bool AndroidNdkMissing => Issues.Any(i => i.Kind == ReadinessIssueKind.AndroidNdkMissing);

    /// <summary>
    /// Evaluates a detected environment.
    /// </summary>
    /// <param name="toolchain">The environment returned by <see cref="RustToolchainDetector"/>.</param>
    /// <param name="requirements">What the build needs, or <c>null</c> for
    /// <see cref="ToolchainRequirements.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="toolchain"/> is null.</exception>
    public static ToolchainReadiness Evaluate(RustToolchainInfo toolchain, ToolchainRequirements requirements = null)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        requirements ??= ToolchainRequirements.Default;
        var issues = new List<ReadinessIssue>();

        CheckTool(toolchain, RustTool.Cargo, required: true, issues);
        CheckTool(toolchain, RustTool.Rustc, required: true, issues);
        CheckTool(toolchain, RustTool.Rustup, requirements.RequireRustup, issues);
        CheckTool(toolchain, RustTool.Clippy, requirements.RequireClippy, issues);
        CheckTool(toolchain, RustTool.Rustfmt, requirements.RequireRustfmt, issues);
        CheckTool(toolchain, RustTool.CCompiler, requirements.RequireCCompiler, issues);

        if (requirements.MinimumRustVersion != null)
        {
            foreach (var tool in new[] { RustTool.Rustc, RustTool.Cargo })
            {
                var installed = toolchain.GetTool(tool);
                if (installed.IsAvailable && installed.Version != null && installed.Version < requirements.MinimumRustVersion)
                {
                    issues.Add(new ReadinessIssue
                    {
                        Kind = ReadinessIssueKind.ToolOutdated,
                        Tool = tool,
                        Subject = installed.Version.ToString(),
                        Message = $"{tool} {installed.Version} is older than the required {requirements.MinimumRustVersion}.",
                        IsBlocking = true,
                    });
                }
            }
        }

        foreach (var target in requirements.RequiredTargets.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(target, toolchain.HostTriple, StringComparison.Ordinal)
                || toolchain.InstalledTargets.Contains(target, StringComparer.Ordinal))
            {
                continue;
            }

            issues.Add(new ReadinessIssue
            {
                Kind = ReadinessIssueKind.TargetMissing,
                Subject = target,
                Message = toolchain.IsRustupManaged
                    ? $"The compilation target '{target}' is not installed."
                    : $"The compilation target '{target}' cannot be verified or added without rustup.",
                IsBlocking = true,
            });
        }

        if (requirements.RequireAndroidNdk && toolchain.AndroidNdk == null)
        {
            issues.Add(new ReadinessIssue
            {
                Kind = ReadinessIssueKind.AndroidNdkMissing,
                Subject = AndroidNdkLocator.HostTag,
                Message = AndroidNdkLocator.HostSupportMessage
                    ?? "No Android NDK was found: set ANDROID_NDK_HOME to an NDK, or ANDROID_HOME to an SDK that has one under ndk/.",
                IsBlocking = true,
            });
        }

        foreach (var pair in requirements.RequiredLinuxCrossTools.Where(p => p.Value != null && !string.IsNullOrWhiteSpace(p.Key)))
        {
            var missing = new[] { pair.Value.CompilerPath, pair.Value.ArchiverPath }.Where(t => !CrossToolExists(t)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var triple = pair.Key.Trim();
            issues.Add(new ReadinessIssue
            {
                Kind = ReadinessIssueKind.LinuxCrossToolMissing,
                Subject = triple,
                Message = $"The cross tool{(missing.Count == 1 ? string.Empty : "s")} {string.Join(" and ", missing.Select(m => "'" + m + "'"))} for '{triple}' {(missing.Count == 1 ? "was" : "were")} not found{(pair.Value.BinDirectory == null ? " on PATH" : string.Empty)}.",
                IsBlocking = true,
            });
        }

        var channel = toolchain.ToolchainFile?.Channel;
        if (requirements.RequireToolchainFileChannel
            && !string.IsNullOrWhiteSpace(channel)
            && toolchain.IsRustupManaged
            && toolchain.InstalledToolchains.Count > 0
            && !toolchain.InstalledToolchains.Any(t => ToolchainMatches(t, channel)))
        {
            issues.Add(new ReadinessIssue
            {
                Kind = ReadinessIssueKind.ToolchainNotInstalled,
                Subject = channel,
                Message = $"The project's {Path(toolchain.ToolchainFile)} asks for toolchain '{channel}', which is not installed.",
                IsBlocking = true,
            });
        }

        var ordered = issues.OrderByDescending(i => i.IsBlocking).ToList();
        return new ToolchainReadiness(toolchain, requirements, ordered);
    }

    /// <summary>
    /// A multi-line summary: <c>Ready</c> or <c>Not ready</c>, then one line per issue.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder(IsReady ? "Ready to build." : "Not ready to build.");
        foreach (var issue in Issues)
        {
            builder.AppendLine().Append("  ").Append(issue);
        }

        return builder.ToString();
    }

    private static void CheckTool(RustToolchainInfo toolchain, RustTool tool, bool required, List<ReadinessIssue> issues)
    {
        if (!required)
        {
            return;
        }

        var installed = toolchain.GetTool(tool);
        switch (installed.Status)
        {
            case ToolStatus.Missing:
                issues.Add(new ReadinessIssue
                {
                    Kind = ReadinessIssueKind.ToolMissing,
                    Tool = tool,
                    Message = $"{tool} is not installed" + (string.IsNullOrEmpty(installed.Message) ? "." : $": {installed.Message}"),
                    IsBlocking = true,
                });
                break;
            case ToolStatus.Unknown:
                issues.Add(new ReadinessIssue
                {
                    Kind = ReadinessIssueKind.ToolStatusUnknown,
                    Tool = tool,
                    Message = $"{tool} could not be checked" + (string.IsNullOrEmpty(installed.Message) ? "." : $": {installed.Message}"),
                    IsBlocking = false,
                });
                break;
        }
    }

    private static bool CrossToolExists(string path)
        => new ExecutableLocator(null).Locate(path) != null;

    private static bool ToolchainMatches(string installedName, string channel)
        => string.Equals(installedName, channel, StringComparison.OrdinalIgnoreCase)
            || installedName.StartsWith(channel + "-", StringComparison.OrdinalIgnoreCase);

    private static string Path(RustToolchainFile file)
        => file?.FilePath == null ? RustToolchainFile.FileName : System.IO.Path.GetFileName(file.FilePath);
}
