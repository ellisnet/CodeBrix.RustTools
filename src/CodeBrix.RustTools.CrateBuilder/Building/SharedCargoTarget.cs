using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Containers;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// The shared cargo target folder of <see cref="CrateBuilderOptions.CargoTargetDirectory"/>:
/// its layout, and how every cargo run CrateBuilder issues is pointed at it.
/// </summary>
/// <remarks>
/// Layout below the folder (each sub-folder is cargo's target folder for its builds):
/// <list type="number">
/// <item><c>host/</c> - host-mode builds of the crate and of a generated facade (they share
/// it, so the facade build reuses the dependency builds of the crate's own build; each
/// build's native file is copied out as soon as cargo finishes, so the facade's library -
/// it has the crate's file name - replacing the crate's own in the folder does no
/// harm).</item>
/// <item><c>docker/</c> - the same for Docker mode in the rust image
/// (<see cref="LinuxBaseline.RustImage"/>), bind-mounted into the build container at
/// <see cref="ContainerPath"/>. Kept apart from <c>host/</c> because the container's
/// toolchain, paths and C library differ from the host's (a build script built for one
/// must never be run by the other). Its cross targets land in cargo's own
/// <c>&lt;triple&gt;/</c> folders below it.</item>
/// <item><c>docker/manylinux-&lt;arch&gt;/</c> (<c>x86_64</c>, <c>aarch64</c>, <c>riscv64</c>) -
/// the <see cref="LinuxBaseline.ManyLinux"/> builds, one folder per manylinux image
/// (<see cref="ManyLinuxFolderFor"/>). cargo's target-triple folder is NOT enough to keep them
/// apart: each manylinux container builds its own architecture natively, without
/// <c>--target</c>, so all of them - and the rust image's native build - would write
/// <c>release/</c>; and a build script compiled against one image's glibc must never be run
/// in another image (or under another architecture).</item>
/// <item><c>api-extractor/</c> - the API extractor tool built with the host
/// toolchain.</item>
/// <item><c>api-extractor-docker/</c> - the API extractor tool built in a container (bind-mounted
/// like <c>docker/</c>).</item>
/// </list>
/// cargo is pointed at a folder through <see cref="VariableName"/> in the run's environment (it
/// wins over the consumer's own environment and over a <c>build.target-dir</c> in a cargo
/// configuration file). A Linux engine creates the files in a bind-mounted folder as root, so
/// after the container's last cargo run they are handed back to the owner of the folder
/// (<see cref="RestoreOwnershipAsync"/>).
/// </remarks>
internal static class SharedCargoTarget
{
    /// <summary>The environment variable cargo reads its target folder from.</summary>
    public const string VariableName = "CARGO_TARGET_DIR";

    /// <summary>Host-mode crate and facade builds.</summary>
    public const string HostFolderName = "host";

    /// <summary>Docker-mode crate and facade builds.</summary>
    public const string DockerFolderName = "docker";

    /// <summary>The prefix of the per-image sub-folders of <c>docker/</c> that the ManyLinux
    /// builds use.</summary>
    public const string ManyLinuxFolderPrefix = "manylinux-";

    /// <summary>The API extractor tool built on the host.</summary>
    public const string ExtractorHostFolderName = "api-extractor";

    /// <summary>The API extractor tool built in a container.</summary>
    public const string ExtractorDockerFolderName = "api-extractor-docker";

    /// <summary>Where a sub-folder is bind-mounted in a build container (outside <c>/work</c>,
    /// so copying the source into <c>/work</c> never touches it).</summary>
    public const string ContainerPath = "/codebrix-cargo-target";

    /// <summary>The full path of <see cref="CrateBuilderOptions.CargoTargetDirectory"/>, or
    /// <c>null</c> when it is not set.</summary>
    public static string Root(CrateBuilderOptions options)
        => options == null || string.IsNullOrWhiteSpace(options.CargoTargetDirectory)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.CargoTargetDirectory.Trim()));

    /// <summary>A sub-folder of the shared folder, or <c>null</c> when it is not set.</summary>
    public static string For(CrateBuilderOptions options, string folderName)
    {
        var root = Root(options);
        return root == null ? null : Path.Combine(root, folderName);
    }

    /// <summary>The cargo target folder of a ManyLinux build of <paramref name="target"/>:
    /// <c>docker/manylinux-&lt;arch&gt;/</c> below the shared folder, or <c>null</c> when the
    /// shared folder is not set.</summary>
    public static string ManyLinuxFolderFor(CrateBuilderOptions options, CrateTarget target)
    {
        var docker = For(options, DockerFolderName);
        return docker == null ? null : Path.Combine(docker, ManyLinuxFolderPrefix + ManyLinux.ManyLinuxImages.ArchitectureName(target));
    }

    /// <summary>Points a host cargo run at <paramref name="hostDirectory"/>; nothing when it is
    /// <c>null</c>. cargo creates the folder (with its <c>CACHEDIR.TAG</c>) when it is
    /// missing.</summary>
    public static void ApplyHost(CargoBuildOptions options, string hostDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (hostDirectory == null)
        {
            return;
        }

        options.Environment[VariableName] = hostDirectory;
    }

    /// <summary>Points a cargo run in a container at <see cref="ContainerPath"/> when
    /// <paramref name="hostDirectory"/> is set (the Docker package lets the run's environment
    /// replace its own <c>CARGO_TARGET_DIR=/work/target</c>).</summary>
    public static void ApplyContainer(CargoBuildOptions options, string hostDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (hostDirectory != null)
        {
            options.Environment[VariableName] = ContainerPath;
        }
    }

    /// <summary>Adds the bind mount of <paramref name="hostDirectory"/> (created if missing) at
    /// <see cref="ContainerPath"/> to a container's settings; nothing when it is <c>null</c>.
    /// Returns <paramref name="options"/>.</summary>
    public static BuildContainerOptions Mount(BuildContainerOptions options, string hostDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (hostDirectory == null)
        {
            return options;
        }

        Directory.CreateDirectory(hostDirectory);
        options.Mounts.Add(MountSpec.Bind(hostDirectory, ContainerPath));
        return options;
    }

    /// <summary>The command that gives every file below <see cref="ContainerPath"/> the owner
    /// and group of the mounted folder itself (the host user that created it).</summary>
    public static string[] OwnershipCommand()
        => new[] { "sh", "-c", "chown -R \"$(stat -c %u:%g " + ContainerPath + ")\" " + ContainerPath };

    /// <summary>
    /// Hands the files cargo created in the mounted folder back to the folder's owner (a Linux
    /// engine creates them as root). Best effort: returns <c>null</c> when it worked, else a
    /// warning for the result.
    /// </summary>
    public static async Task<string> RestoreOwnershipAsync(DockerRustWorkbench workbench, BuildContainer container, string hostDirectory)
    {
        if (hostDirectory == null || workbench == null || container == null || container.IsRemoved)
        {
            return null;
        }

        try
        {
            await workbench.ExecAsync(container, OwnershipCommand(), CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"The files the build container wrote to the shared cargo target folder {hostDirectory} could not be handed back to the folder's owner: {ex.Message}";
        }
    }
}
