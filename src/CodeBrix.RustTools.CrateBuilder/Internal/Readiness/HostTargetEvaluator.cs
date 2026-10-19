using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// Decides, from a detected toolchain, whether this host can build a target - through the
/// requirement and readiness APIs of CodeBrix.RustTools, never by running anything itself:
/// <list type="number">
/// <item>the host's own triple: buildable when the base check (cargo, rustc, C toolchain)
/// passes;</item>
/// <item>another Linux triple: <see cref="ToolchainRequirements.ForLinuxCross"/> (the
/// target's standard library and the GNU cross gcc and ar);</item>
/// <item>Android: the target's standard library and an NDK
/// (<see cref="ToolchainRequirements.RequireAndroidNdk"/>);</item>
/// <item>macOS: both the target's standard library and, on a host that is not a Mac, the
/// macOS link profile (<see cref="MacOSLinkProfile.ForToolchain"/> with
/// <see cref="CrateBuilderOptions.MacOSSysrootDirectory"/>);</item>
/// <item>Windows: <see cref="ToolchainRequirements.ForWindowsGnullvm"/> (the gnullvm
/// target's standard library and llvm-mingw's clang wrapper, found by
/// <see cref="LlvmMingwLocator"/> in <see cref="CrateBuilderOptions.LlvmMingwDirectory"/>, else
/// <c>LLVM_MINGW_HOME</c> or PATH), with the host package's install guidance.</item>
/// </list>
/// </summary>
internal static class HostTargetEvaluator
{
    public static TargetEvaluation Evaluate(CrateTarget target, RustToolchainInfo toolchain, HostEvaluationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        settings ??= new HostEvaluationSettings();
        var evaluation = new TargetEvaluation { Target = target, IsBuildable = true };
        var triple = CrateTargets.Triple(target);
        if (string.Equals(triple, toolchain.HostTriple, StringComparison.Ordinal))
        {
            return evaluation;
        }

        var hostOs = settings.HostOperatingSystem ?? toolchain.OperatingSystem;
        ToolchainRequirements requirements;
        if (CrateTargets.IsLinux(target))
        {
            requirements = ToolchainRequirements.ForLinuxCross(new[] { triple }, settings.LinuxCrossToolDirectory);
        }
        else if (CrateTargets.IsWindows(target))
        {
            requirements = ToolchainRequirements.ForWindowsGnullvm(new[] { triple }, settings.LlvmMingwDirectory);
        }
        else
        {
            requirements = new ToolchainRequirements();
            requirements.RequiredTargets.Add(triple);
            requirements.RequireAndroidNdk = CrateTargets.IsAndroid(target);
        }

        requirements.RequireCCompiler = false;
        requirements.RequireToolchainFileChannel = false;
        var readiness = ToolchainReadiness.Evaluate(toolchain, requirements);
        foreach (var issue in readiness.Issues.Where(i => i.IsBlocking))
        {
            evaluation.IsBuildable = false;
            evaluation.Issues.Add(issue.Kind == ReadinessIssueKind.LlvmMingwMissing
                ? new BuildReadinessIssue { Code = LlvmMingwMissingCode, Message = issue.Message + " Set CrateBuilderOptions.LlvmMingwDirectory or " + LlvmMingwLocator.HomeVariable + " to llvm-mingw's folder, or put its bin folder on PATH.", Target = target }
                : FromToolchainIssue(issue, "target", target));
        }

        evaluation.Guidance.AddRange(InstallGuidanceProvider.ForReadiness(readiness));

        if (CrateTargets.IsMacOS(target) && hostOs != OperatingSystemKind.MacOS)
        {
            var problem = CheckMacOSLinkProfile(toolchain, settings);
            if (problem != null)
            {
                evaluation.IsBuildable = false;
                evaluation.Issues.Add(new BuildReadinessIssue { Code = "macos.link-profile-unavailable", Message = problem, Target = target });
            }
        }

        return evaluation;
    }

    /// <summary>The issue code of a Windows target whose llvm-mingw was not found (from
    /// <see cref="ReadinessIssueKind.LlvmMingwMissing"/>).</summary>
    public const string LlvmMingwMissingCode = "windows.llvm-mingw-missing";

    /// <summary>Maps a CodeBrix.RustTools readiness issue: Code = prefix + "." + the kind in
    /// kebab case (for example <c>toolchain.tool-missing</c>).</summary>
    public static BuildReadinessIssue FromToolchainIssue(ReadinessIssue issue, string prefix, CrateTarget? target)
        => new BuildReadinessIssue
        {
            Code = prefix + "." + Kebab(issue.Kind.ToString()),
            Message = issue.Message,
            IsBlocking = target == null && issue.IsBlocking,
            Target = target,
        };

    private static string CheckMacOSLinkProfile(RustToolchainInfo toolchain, HostEvaluationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MacOSSysrootDirectory))
        {
            return "Building macOS targets on a host that is not a Mac needs the macOS link profile's sysroot: export it from a CodeBrix.RustTools.Docker image with the macOS layer (DockerRustWorkbench.ExportMacOSSysrootAsync) and set CrateBuilderOptions.MacOSSysrootDirectory to that folder, or build in Docker mode.";
        }

        try
        {
            MacOSLinkProfile.ForToolchain(toolchain, settings.MacOSSysrootDirectory);
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or ArgumentException or InvalidOperationException)
        {
            return "The macOS link profile cannot be used: " + ex.Message;
        }
    }

    private static string Kebab(string name)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
