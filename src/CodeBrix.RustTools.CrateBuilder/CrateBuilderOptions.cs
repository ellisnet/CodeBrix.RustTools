using System.IO;
using CodeBrix.RustTools.Docker;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Settings shared by every build a <see cref="CrateBuilder"/> runs.
/// </summary>
public sealed class CrateBuilderOptions
{
    /// <summary>The folder name used under the system temp folder when
    /// <see cref="ScratchRootDirectory"/> is <c>null</c>.</summary>
    public const string DefaultScratchFolderName = "CodeBrix.RustTools.CrateBuilder";

    /// <summary>Settings of the host-mode workbench (CodeBrix.RustTools); <c>null</c> = the
    /// defaults.</summary>
    public RustWorkbenchOptions HostOptions { get; set; }

    /// <summary>Settings of the Docker-mode workbench (CodeBrix.RustTools.Docker); <c>null</c>
    /// = the defaults.</summary>
    public DockerRustWorkbenchOptions DockerOptions { get; set; }

    /// <summary>Where staged workspaces, clones and per-run folders go; <c>null</c> =
    /// <c>&lt;temp&gt;/CodeBrix.RustTools.CrateBuilder</c>.</summary>
    public string ScratchRootDirectory { get; set; }

    /// <summary>Keep each run's scratch folder (the staged workspace, the facade crate) for
    /// inspection instead of deleting it after the build. Default false.</summary>
    public bool KeepScratch { get; set; }

    /// <summary>Host mode, Windows targets: llvm-mingw's folder (the one holding
    /// <c>bin/</c>) or its <c>bin/</c> folder itself; <c>null</c> = the
    /// <c>LLVM_MINGW_HOME</c> environment variable, then the clang wrappers on PATH.</summary>
    public string LlvmMingwDirectory { get; set; }

    /// <summary>Host mode on a machine that is not a Mac, macOS targets: the folder holding
    /// <c>usr/lib/libSystem.tbd</c> for CodeBrix.RustTools' macOS link profile (export it from
    /// a CodeBrix.RustTools.Docker image with the macOS layer through
    /// <c>DockerRustWorkbench.ExportMacOSSysrootAsync</c>); <c>null</c> = none, so macOS
    /// targets are not buildable in host mode there.</summary>
    public string MacOSSysrootDirectory { get; set; }

    /// <summary>Requests with <see cref="CrateBuildRequest.LinkToCodeBrixWebGpu"/>: the
    /// CodeBrix.WebGpu.Native package folder to take <c>include/</c> and
    /// <c>runtimes/&lt;rid&gt;/native/</c> from (the folder holding both, for example
    /// <c>~/.nuget/packages/codebrix.webgpu.native.mitlicenseforever/&lt;version&gt;</c>);
    /// <c>null</c> = the NuGet global packages folder (<c>NUGET_PACKAGES</c>, else
    /// <c>~/.nuget/packages</c>), then, for the native libraries only, the application's own
    /// <c>runtimes/</c> folder. The NuGet folder is searched for the package version
    /// CrateBuilder references; an explicit folder of another version is used as given and
    /// the difference is noted.</summary>
    public string WebGpuNativePackageDirectory { get; set; }

    /// <summary>The scratch root that applies: <see cref="ScratchRootDirectory"/>, else the
    /// default under the system temp folder.</summary>
    public string EffectiveScratchRootDirectory
        => string.IsNullOrWhiteSpace(ScratchRootDirectory)
            ? Path.Combine(Path.GetTempPath(), DefaultScratchFolderName)
            : Path.GetFullPath(ScratchRootDirectory);
}
