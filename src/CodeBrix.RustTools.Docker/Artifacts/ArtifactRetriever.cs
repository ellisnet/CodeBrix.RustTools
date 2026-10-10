using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// Copies the files cargo reported (<see cref="BuildArtifact.Executable"/> and
/// <see cref="BuildArtifact.Filenames"/>, which are container paths) out of a build container
/// into a host folder, in the requested <see cref="ArtifactLayout"/>, and records each file's
/// size, SHA-256 and execute bit.
/// </summary>
internal static class ArtifactRetriever
{
    /// <summary>
    /// One file chosen for retrieval.
    /// </summary>
    internal sealed record Selection(string ContainerPath, ArtifactKind Kind, BuildArtifact Artifact);

    /// <summary>
    /// Chooses the files to copy: build scripts, proc-macro crates (host-side compiler plugins),
    /// test harnesses and <c>.rmeta</c>/<c>.d</c> files are never copied; the rest are filtered
    /// by kind according to the options. For a cross target (any target that is not the
    /// engine's own Linux architecture) only files under the target's own output folder
    /// (<c>target/&lt;triple&gt;/</c>) are chosen, so host-compiled helper crates under
    /// <c>target/&lt;profile&gt;/</c> are left behind. A path reported twice (a fresh and a
    /// rebuilt artifact) is chosen once. Skipped files are not reported.
    /// </summary>
    /// <param name="artifacts">The artifacts cargo reported for the target.</param>
    /// <param name="options">The options; <c>null</c> means the defaults.</param>
    /// <param name="target">The target the artifacts were compiled for
    /// (<see cref="CrossTarget.LinuxNative"/> counts as native).</param>
    /// <param name="nativeLinuxTriple">The engine's own Linux triple; <c>null</c> treats every
    /// concrete Linux target as native (the artifacts then sit in <c>target/&lt;profile&gt;/</c>).</param>
    public static IReadOnlyList<Selection> Select(IEnumerable<BuildArtifact> artifacts, ArtifactRetrievalOptions options, CrossTarget target = CrossTarget.LinuxNative, string nativeLinuxTriple = null)
    {
        if (artifacts == null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        options ??= new ArtifactRetrievalOptions();
        var info = CrossTargetInfo.Get(target);
        var native = info.IsLinuxNative || (info.IsLinux && (nativeLinuxTriple == null || info.Triple == nativeLinuxTriple));
        var crossSegment = native ? null : "/" + info.Triple + "/";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Selection>();
        foreach (var artifact in artifacts)
        {
            if (artifact == null || artifact.IsBuildScript || artifact.Profile.Test || IsProcMacro(artifact))
            {
                continue;
            }

            var paths = new List<string>();
            if (artifact.Executable != null)
            {
                paths.Add(artifact.Executable);
            }

            paths.AddRange(artifact.Filenames.Where(f => !string.IsNullOrWhiteSpace(f)));
            foreach (var path in paths)
            {
                if (crossSegment != null && !path.Contains(crossSegment, StringComparison.Ordinal))
                {
                    continue;
                }

                var kind = Classify(path, artifact);
                if (Include(kind, options) && seen.Add(path))
                {
                    result.Add(new Selection(path, kind, artifact));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Says what a file is from its name (and whether it is the artifact's executable).
    /// </summary>
    public static ArtifactKind Classify(string containerPath, BuildArtifact artifact)
    {
        if (artifact != null && string.Equals(containerPath, artifact.Executable, StringComparison.Ordinal))
        {
            return ArtifactKind.Executable;
        }

        var name = FileName(containerPath).ToLowerInvariant();
        if (name.EndsWith(".rlib", StringComparison.Ordinal))
        {
            return ArtifactKind.Rlib;
        }

        if (name.EndsWith(".so", StringComparison.Ordinal) || name.EndsWith(".dll", StringComparison.Ordinal) || name.EndsWith(".dylib", StringComparison.Ordinal))
        {
            return ArtifactKind.SharedLibrary;
        }

        if (name.EndsWith(".a", StringComparison.Ordinal) || name.EndsWith(".lib", StringComparison.Ordinal))
        {
            return ArtifactKind.StaticLibrary;
        }

        if (name.EndsWith(".exe", StringComparison.Ordinal))
        {
            return ArtifactKind.Executable;
        }

        return ArtifactKind.Other;
    }

    /// <summary>
    /// The folder, relative to the host folder, that receives a target's files in a layout
    /// (with <c>/</c> separators): empty for <see cref="ArtifactLayout.Flat"/>, the triple for
    /// <see cref="ArtifactLayout.ByTarget"/>, <c>runtimes/&lt;rid&gt;/native</c> for
    /// <see cref="ArtifactLayout.NuGetRuntimes"/>. The target must be concrete:
    /// <see cref="CrossTarget.LinuxNative"/> is resolved by the workbench before retrieval.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is
    /// <see cref="CrossTarget.LinuxNative"/>.</exception>
    public static string RelativeDirectory(ArtifactLayout layout, CrossTarget target)
    {
        var info = CrossTargetInfo.Get(target);
        if (info.IsLinuxNative)
        {
            throw new ArgumentException("LinuxNative must be resolved to the engine's Linux target before retrieval (CrossTargetInfo.Resolve).", nameof(target));
        }

        return layout switch
        {
            ArtifactLayout.Flat => string.Empty,
            ArtifactLayout.ByTarget => info.Triple,
            ArtifactLayout.NuGetRuntimes => "runtimes/" + info.RuntimeIdentifier + "/native",
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown artifact layout."),
        };
    }

    /// <summary>
    /// Checks the options before anything is copied.
    /// </summary>
    public static void Validate(ArtifactRetrievalOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.HostDirectory))
        {
            throw new ArgumentException("ArtifactRetrievalOptions.HostDirectory must name the host folder that receives the files.", nameof(options));
        }

        if (!Enum.IsDefined(options.Layout))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Layout, "Unknown artifact layout.");
        }
    }

    /// <summary>
    /// Copies the chosen files of one target out of the container. <paramref name="engine"/>
    /// tells which Linux target is native (its artifacts sit in <c>target/&lt;profile&gt;/</c>,
    /// a cross target's in <c>target/&lt;triple&gt;/</c>); the target must be concrete.
    /// </summary>
    public static async Task<ArtifactRetrievalResult> RetrieveAsync(
        IContainerFileTransport transport,
        IEnumerable<BuildArtifact> artifacts,
        ArtifactRetrievalOptions options,
        CrossTarget target,
        DockerEngineInfo engine,
        CancellationToken cancellationToken)
    {
        Validate(options);
        var root = Path.GetFullPath(options.HostDirectory);
        var relative = RelativeDirectory(options.Layout, target);
        var destination = relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(destination);

        var retrieved = new List<RetrievedArtifact>();
        foreach (var selection in Select(artifacts, options, target, engine?.NativeLinuxTriple))
        {
            var hostPath = Path.Combine(destination, FileName(selection.ContainerPath));
            if (!options.Overwrite && File.Exists(hostPath))
            {
                throw new ArtifactRetrievalException(
                    $"'{hostPath}' already exists and ArtifactRetrievalOptions.Overwrite is false.",
                    selection.ContainerPath);
            }

            var file = await CopyFileOutAsync(transport, selection.ContainerPath, destination, cancellationToken).ConfigureAwait(false);
            retrieved.Add(new RetrievedArtifact
            {
                HostPath = file.HostPath,
                ContainerPath = selection.ContainerPath,
                Target = target,
                Kind = selection.Kind,
                Size = file.Size,
                Sha256 = await ComputeSha256Async(file.HostPath, cancellationToken).ConfigureAwait(false),
                IsExecutable = file.IsExecutable,
                PackageName = selection.Artifact.PackageName ?? string.Empty,
                TargetName = selection.Artifact.TargetName ?? string.Empty,
            });
        }

        return new ArtifactRetrievalResult { HostDirectory = root, Artifacts = retrieved };
    }

    /// <summary>
    /// Joins the files of the two macOS slice builds into universal files in the layout folder
    /// of <see cref="CrossTarget.MacOSUniversal"/>. A file is joined when both builds produced
    /// one of that name (after the same selection rules as <see cref="Select"/>); the slices
    /// are copied to a temporary folder first and removed afterwards.
    /// </summary>
    public static async Task<ArtifactRetrievalResult> RetrieveUniversalAsync(
        IContainerFileTransport transport,
        IEnumerable<BuildArtifact> arm64Artifacts,
        IEnumerable<BuildArtifact> x64Artifacts,
        ArtifactRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        Validate(options);
        var root = Path.GetFullPath(options.HostDirectory);
        var relative = RelativeDirectory(options.Layout, CrossTarget.MacOSUniversal);
        var destination = relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(destination);

        var arm64 = Select(arm64Artifacts, options, CrossTarget.MacOSArm64).ToDictionary(s => FileName(s.ContainerPath), StringComparer.Ordinal);
        var x64 = Select(x64Artifacts, options, CrossTarget.MacOSX64).ToDictionary(s => FileName(s.ContainerPath), StringComparer.Ordinal);
        var temp = Path.Combine(Path.GetTempPath(), "CodeBrix.RustTools.Docker", "universal-" + Guid.NewGuid().ToString("N"));
        var retrieved = new List<RetrievedArtifact>();
        try
        {
            foreach (var name in arm64.Keys.Where(x64.ContainsKey).OrderBy(n => n, StringComparer.Ordinal))
            {
                var hostPath = Path.Combine(destination, name);
                if (!options.Overwrite && File.Exists(hostPath))
                {
                    throw new ArtifactRetrievalException(
                        $"'{hostPath}' already exists and ArtifactRetrievalOptions.Overwrite is false.",
                        arm64[name].ContainerPath);
                }

                var armFile = await CopyFileOutAsync(transport, arm64[name].ContainerPath, Path.Combine(temp, "arm64"), cancellationToken).ConfigureAwait(false);
                var x64File = await CopyFileOutAsync(transport, x64[name].ContainerPath, Path.Combine(temp, "x86_64"), cancellationToken).ConfigureAwait(false);
                IReadOnlyList<MacOSBinarySlice> slices;
                try
                {
                    slices = MacOSUniversalBinary.Create(new[] { armFile.HostPath, x64File.HostPath }, hostPath);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IOException)
                {
                    throw new ArtifactRetrievalException($"'{name}' could not be joined into a universal file: {ex.Message}", arm64[name].ContainerPath, ex);
                }

                var executable = armFile.IsExecutable || x64File.IsExecutable;
                if (executable && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(hostPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                retrieved.Add(new RetrievedArtifact
                {
                    HostPath = hostPath,
                    ContainerPath = arm64[name].ContainerPath,
                    Target = CrossTarget.MacOSUniversal,
                    Kind = arm64[name].Kind,
                    Size = new FileInfo(hostPath).Length,
                    Sha256 = await ComputeSha256Async(hostPath, cancellationToken).ConfigureAwait(false),
                    IsExecutable = executable,
                    PackageName = arm64[name].Artifact.PackageName ?? string.Empty,
                    TargetName = arm64[name].Artifact.TargetName ?? string.Empty,
                });
                _ = slices;
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch (IOException)
            {
                // A temporary folder that cannot be deleted is not worth failing the retrieval for.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }

        return new ArtifactRetrievalResult { HostDirectory = root, Artifacts = retrieved };
    }

    /// <summary>Copies one container file into a host folder and returns it.</summary>
    private static async Task<TransportedFile> CopyFileOutAsync(IContainerFileTransport transport, string containerPath, string hostDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(hostDirectory);
        var copied = await transport.CopyOutAsync(new[] { containerPath }, hostDirectory, overwrite: true, cancellationToken).ConfigureAwait(false);
        return copied.FirstOrDefault(f => string.Equals(f.ContainerPath, containerPath, StringComparison.Ordinal))
            ?? copied.FirstOrDefault()
            ?? throw new ArtifactRetrievalException($"'{containerPath}' is not a regular file in the build container.", containerPath);
    }

    /// <summary>The SHA-256 of a host file, as lowercase hexadecimal.</summary>
    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsProcMacro(BuildArtifact artifact)
        => artifact.Kinds.Any(k => CargoTargetKinds.Parse(k) == CargoTargetKind.ProcMacro);

    private static bool Include(ArtifactKind kind, ArtifactRetrievalOptions options) => kind switch
    {
        ArtifactKind.Executable => options.IncludeExecutables,
        ArtifactKind.SharedLibrary => options.IncludeSharedLibraries,
        ArtifactKind.StaticLibrary => options.IncludeStaticLibraries,
        ArtifactKind.Rlib => options.IncludeRlibs,
        _ => false,
    };

    private static string FileName(string containerPath)
    {
        var trimmed = (containerPath ?? string.Empty).TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed.Substring(slash + 1);
    }
}
