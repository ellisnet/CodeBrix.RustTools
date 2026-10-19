using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.Licensing;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// The cargo options of one target's build, from the request: profile, features, offline /
/// locked, the licence gate (Enforce, as a failed result rather than an exception), the
/// timeout and <c>-p &lt;package&gt;</c>.
/// </summary>
/// <remarks>
/// A generated facade crate is built differently in two ways: the request's features are
/// already written into the facade's dependency on the upstream crate (the facade itself
/// declares none, so <c>--features</c> would be refused), and <c>--locked</c> is never passed
/// (the facade's Cargo.lock is a copy of the upstream lock, which has no entry for the facade
/// package itself, so cargo must add it). <see cref="CrateBuildRequest.Offline"/> still
/// applies.
/// </remarks>
internal static class CargoOptionsFactory
{
    public static CargoBuildOptions Create(CrateBuildRequest request, BuiltCrate crate, bool isFacade = false)
    {
        var options = new CargoBuildOptions
        {
            Profile = request.Profile == CrateBuildProfile.Debug ? CargoProfile.Debug : CargoProfile.Release,
            NoDefaultFeatures = !isFacade && request.NoDefaultFeatures,
            AllFeatures = !isFacade && request.AllFeatures,
            Offline = request.Offline,
            Locked = !isFacade && request.Locked,
            LicensePolicy = request.LicensePolicy?.Clone(),
            LicenseGateMode = LicenseGateMode.Enforce,
            ThrowOnLicenseViolation = false,
            Timeout = request.Timeout,
        };
        foreach (var feature in isFacade ? new List<string>() : request.Features ?? new List<string>())
        {
            options.Features.Add(feature.Trim());
        }

        options.Packages.Add(crate.PackageName);
        return options;
    }

    /// <summary>
    /// The cargo configuration file of the staged upstream workspace (<c>.cargo/config.toml</c>,
    /// or the older <c>.cargo/config</c>) as a path RELATIVE to the facade's folder, for
    /// <c>cargo --config</c>; <c>null</c> when the build is not a facade or there is none.
    /// cargo finds a configuration only in the folders above the one it runs in, and the facade
    /// folder is not below the upstream workspace, so without this the upstream's
    /// <c>[patch]</c>, <c>[source]</c> replacement and <c>[env]</c> settings would be lost. The
    /// path is relative because cargo runs in the facade's folder in both modes and the whole
    /// staging root is copied into the container; cargo resolves the file's own relative paths
    /// against the upstream workspace, as it would there.
    /// </summary>
    public static string UpstreamConfigArgument(StagedWorkspace workspace)
    {
        if (workspace == null || !workspace.IsFacade || string.IsNullOrEmpty(workspace.WorkspaceManifestPath) || string.IsNullOrEmpty(workspace.ManifestPath))
        {
            return null;
        }

        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspace.WorkspaceManifestPath)), StagingFileSystem.CargoConfigFolderName);
        var file = new[] { "config.toml", "config" }.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);
        if (file == null)
        {
            return null;
        }

        return Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(workspace.ManifestPath)), file).Replace('\\', '/');
    }

    /// <summary>Adds <c>--config &lt;file&gt;</c> for <see cref="UpstreamConfigArgument"/>
    /// when there is one.</summary>
    public static void AddUpstreamConfig(CargoBuildOptions options, StagedWorkspace workspace)
    {
        var config = UpstreamConfigArgument(workspace);
        if (config != null)
        {
            options.ExtraArguments.Add("--config");
            options.ExtraArguments.Add(config);
        }
    }

    /// <summary>The macOS install name of the crate's dylib: <c>@rpath/lib&lt;name&gt;.dylib</c>,
    /// so the file does not record its build path.</summary>
    public static string MacOSInstallName(BuiltCrate crate) => "@rpath/lib" + crate.LibraryName + ".dylib";

    /// <summary>The name of cargo's per-target rustflags variable,
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c>.</summary>
    public static string RustFlagsVariable(string triple)
        => "CARGO_TARGET_" + triple.ToUpperInvariant().Replace('-', '_').Replace('.', '_') + "_RUSTFLAGS";
}
