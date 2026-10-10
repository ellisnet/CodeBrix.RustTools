using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// A Rust (cargo) project on disk: its Cargo.toml, its Cargo.lock, its workspace, its targets,
/// and - through <see cref="GetMetadataAsync"/> - cargo's fully resolved dependency graph.
/// Opening a project reads files only; nothing is run until <see cref="GetMetadataAsync"/>.
/// </summary>
public sealed class CargoProject
{
    private static readonly TimeSpan DefaultMetadataTimeout = TimeSpan.FromMinutes(5);

    private readonly Lazy<CargoManifest> _workspaceRootManifest;

    private CargoProject(string manifestPath, CargoManifest manifest, RustWorkbenchOptions options)
    {
        ManifestPath = manifestPath;
        Manifest = manifest;
        Options = options ?? new RustWorkbenchOptions();
        WorkspaceRootManifestPath = manifest.IsWorkspaceRoot ? manifestPath : CargoProjectLocator.FindWorkspaceRootManifest(Directory);
        _workspaceRootManifest = new Lazy<CargoManifest>(() =>
            WorkspaceRootManifestPath == null ? null
            : CargoProjectLocator.PathEquals(WorkspaceRootManifestPath, ManifestPath) ? Manifest
            : CargoProjectLocator.TryLoad(WorkspaceRootManifestPath));

        var lockDirectory = WorkspaceRootManifestPath == null ? Directory : Path.GetDirectoryName(WorkspaceRootManifestPath);
        var lockPath = Path.Combine(lockDirectory, CargoLockFile.FileName);
        LockFile = File.Exists(lockPath) ? CargoLockFile.Load(lockPath) : null;
    }

    /// <summary>The full path of the project's Cargo.toml.</summary>
    public string ManifestPath { get; }

    /// <summary>The project folder (the folder containing <see cref="ManifestPath"/>).</summary>
    public string Directory => Path.GetDirectoryName(ManifestPath);

    /// <summary>The parsed Cargo.toml.</summary>
    public CargoManifest Manifest { get; }

    /// <summary>The parsed Cargo.lock of the project (for a workspace member, the workspace
    /// root's lock file), or <c>null</c> when there is none yet.</summary>
    public CargoLockFile LockFile { get; }

    /// <summary>The settings used to find and run cargo.</summary>
    public RustWorkbenchOptions Options { get; }

    /// <summary>True when this manifest has a <c>[workspace]</c> table.</summary>
    public bool IsWorkspaceRoot => Manifest.IsWorkspaceRoot;

    /// <summary>True for a virtual workspace manifest (no <c>[package]</c>).</summary>
    public bool IsVirtual => Manifest.IsVirtual;

    /// <summary>The Cargo.toml of the workspace this project belongs to (itself for a workspace
    /// root), or <c>null</c> when it is a standalone package.</summary>
    public string WorkspaceRootManifestPath { get; }

    /// <summary>The workspace root's parsed manifest (this project's own manifest for a root),
    /// or <c>null</c>.</summary>
    public CargoManifest WorkspaceRootManifest => _workspaceRootManifest.Value;

    /// <summary>The package name, or <c>null</c> for a virtual manifest.</summary>
    public string Name => Manifest.Package?.Name;

    /// <summary>
    /// The <c>[package]</c> table with workspace inheritance applied (<c>license.workspace =
    /// true</c> and the like take the workspace root's <c>[workspace.package]</c> values), or
    /// <c>null</c> for a virtual manifest.
    /// </summary>
    public CargoPackage EffectivePackage => Manifest.Package?.WithInheritance(WorkspaceRootManifest?.Workspace?.Package);

    /// <summary>
    /// The member folders of the workspace whose root is this manifest (globs expanded,
    /// excludes removed; the root folder itself first when the root also has a
    /// <c>[package]</c>). Empty when this manifest is not a workspace root.
    /// </summary>
    public IReadOnlyList<string> WorkspaceMemberDirectories
    {
        get
        {
            if (Manifest.Workspace == null)
            {
                return Array.Empty<string>();
            }

            var members = new List<string>();
            if (Manifest.Package != null)
            {
                members.Add(Directory);
            }

            members.AddRange(CargoProjectLocator.ExpandWorkspaceMembers(Directory, Manifest.Workspace)
                .Where(m => !CargoProjectLocator.PathEquals(m, Directory)));
            return members;
        }
    }

    /// <summary>
    /// Opens a project from the path of a Cargo.toml or of a folder containing one.
    /// </summary>
    /// <param name="pathToCargoTomlOrDirectory">A Cargo.toml path, or a folder that contains
    /// Cargo.toml.</param>
    /// <param name="options">Settings used when cargo is run, or <c>null</c> for the defaults.</param>
    /// <exception cref="FileNotFoundException">There is no Cargo.toml at the path.</exception>
    /// <exception cref="RustToolsException">Cargo.toml (or Cargo.lock) is not valid TOML.</exception>
    public static CargoProject Open(string pathToCargoTomlOrDirectory, RustWorkbenchOptions options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathToCargoTomlOrDirectory);
        var fullPath = Path.GetFullPath(pathToCargoTomlOrDirectory);
        var manifestPath = System.IO.Directory.Exists(fullPath) ? Path.Combine(fullPath, CargoManifest.FileName) : fullPath;
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"No {CargoManifest.FileName} was found at '{fullPath}'.", manifestPath);
        }

        return new CargoProject(manifestPath, CargoManifest.Load(manifestPath), options);
    }

    /// <summary>
    /// Opens every workspace member (see <see cref="WorkspaceMemberDirectories"/>) as a project.
    /// </summary>
    public IReadOnlyList<CargoProject> GetWorkspaceMembers()
        => WorkspaceMemberDirectories.Select(d => CargoProjectLocator.PathEquals(d, Directory) ? this : Open(d, Options)).ToList();

    /// <summary>
    /// Lists the package's build targets without running cargo: the targets declared in
    /// Cargo.toml plus those cargo discovers by convention (<c>src/lib.rs</c>,
    /// <c>src/main.rs</c>, <c>src/bin/*.rs</c>, <c>src/bin/*/main.rs</c>,
    /// <c>examples/*.rs</c>, <c>tests/*.rs</c>, <c>benches/*.rs</c>, <c>build.rs</c>), honouring
    /// <c>autobins</c>/<c>autoexamples</c>/<c>autotests</c>/<c>autobenches = false</c>. Missing
    /// names and paths are filled with cargo's defaults. Empty for a virtual manifest.
    /// </summary>
    public IReadOnlyList<CargoTargetDeclaration> GetDeclaredTargets()
    {
        var package = Manifest.Package;
        if (package == null)
        {
            return Array.Empty<CargoTargetDeclaration>();
        }

        var packageName = package.Name ?? Path.GetFileName(Directory);
        var crateName = packageName.Replace('-', '_');
        var result = new List<CargoTargetDeclaration>();

        // Library.
        if (Manifest.Lib != null)
        {
            result.Add(Complete(Manifest.Lib, Manifest.Lib.Name ?? crateName, Manifest.Lib.Path ?? "src/lib.rs"));
        }
        else if (FileExists("src/lib.rs"))
        {
            result.Add(new CargoTargetDeclaration { Name = crateName, Kind = CargoTargetKind.Lib, Path = "src/lib.rs" });
        }

        // Binaries.
        var autoBins = package.AutoBins ?? true;
        var discoveredBins = new List<CargoTargetDeclaration>();
        if (autoBins)
        {
            if (FileExists("src/main.rs"))
            {
                discoveredBins.Add(new CargoTargetDeclaration { Name = packageName, Kind = CargoTargetKind.Bin, Path = "src/main.rs" });
            }

            discoveredBins.AddRange(Discover("src/bin", CargoTargetKind.Bin));
        }

        foreach (var bin in Manifest.Bins)
        {
            var name = bin.Name ?? packageName;
            result.Add(Complete(bin, name, bin.Path ?? DefaultBinPath(name, packageName)));
        }

        AddUndeclared(result, discoveredBins, CargoTargetKind.Bin);

        // Examples, tests, benches.
        AddKind(result, Manifest.Examples, package.AutoExamples ?? true, "examples", CargoTargetKind.Example);
        AddKind(result, Manifest.Tests, package.AutoTests ?? true, "tests", CargoTargetKind.Test);
        AddKind(result, Manifest.Benches, package.AutoBenches ?? true, "benches", CargoTargetKind.Bench);

        // Build script.
        if (!package.BuildScriptDisabled)
        {
            var buildPath = package.BuildScript ?? (FileExists("build.rs") ? "build.rs" : null);
            if (buildPath != null)
            {
                result.Add(new CargoTargetDeclaration
                {
                    Name = "build-script-build",
                    Kind = CargoTargetKind.CustomBuild,
                    Path = buildPath.Replace('\\', '/'),
                    IsDeclared = package.BuildScript != null,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Runs <c>cargo metadata --format-version 1</c> for this manifest and parses the output.
    /// cargo is found the same way as for toolchain detection (the options' paths, PATH,
    /// <c>~/.cargo/bin</c>), and runs with <c>RUSTUP_AUTO_INSTALL=0</c>.
    /// </summary>
    /// <param name="options">Flags for the run (<c>--offline</c>, <c>--locked</c>,
    /// <c>--no-deps</c>, <c>--filter-platform</c>, features), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the run (the cargo process tree is killed).</param>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException">cargo failed (for example it needed the network
    /// with <see cref="CargoMetadataOptions.Offline"/> set) or timed out.</exception>
    /// <exception cref="RustToolsException">cargo's output could not be parsed.</exception>
    public async Task<CargoMetadata> GetMetadataAsync(CargoMetadataOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new CargoMetadataOptions();
        var locator = new ExecutableLocator(Options);
        var cargo = locator.Locate(RustTool.Cargo);
        if (cargo == null)
        {
            var guidance = new[] { InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallRust, HostOperatingSystem.Detect()) };
            throw new ToolchainNotFoundException(
                $"cargo was not found (searched {locator.DescribeSearch()}). Install Rust to read the project's metadata.",
                null,
                guidance);
        }

        var request = new ProcessRunRequest(cargo)
        {
            WorkingDirectory = Directory,
            Timeout = options.Timeout ?? DefaultMetadataTimeout,
        };
        foreach (var argument in BuildMetadataArguments(ManifestPath, options))
        {
            request.Arguments.Add(argument);
        }

        foreach (var pair in RustToolchainDetector.BuildProbeEnvironment(Options))
        {
            request.Environment[pair.Key] = pair.Value;
        }

        if (!request.Environment.ContainsKey("CARGO_TERM_COLOR"))
        {
            request.Environment["CARGO_TERM_COLOR"] = "never";
        }

        var result = await ProcessRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new CargoCommandException($"'{result.CommandLine}' timed out after {request.Timeout}.", null, result.StandardError, Array.Empty<InstallGuidance>());
        }

        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("error", StringComparison.Ordinal))
                ?? result.StandardError.Trim();
            throw new CargoCommandException($"cargo metadata failed (exit code {result.ExitCode}): {error}", result.ExitCode, result.StandardError, Array.Empty<InstallGuidance>());
        }

        return CargoMetadata.Parse(result.StandardOutput);
    }

    /// <inheritdoc />
    public override string ToString() => Name == null ? $"workspace {ManifestPath}" : $"{Name} ({ManifestPath})";

    /// <summary>
    /// Builds the <c>cargo metadata</c> argument list (without the executable).
    /// </summary>
    internal static IReadOnlyList<string> BuildMetadataArguments(string manifestPath, CargoMetadataOptions options)
    {
        var arguments = new List<string> { "metadata", "--format-version", "1", "--manifest-path", manifestPath };
        if (options.Offline)
        {
            arguments.Add("--offline");
        }

        if (options.Locked)
        {
            arguments.Add("--locked");
        }

        if (options.Frozen)
        {
            arguments.Add("--frozen");
        }

        if (options.NoDependencies)
        {
            arguments.Add("--no-deps");
        }

        if (!string.IsNullOrWhiteSpace(options.FilterPlatform))
        {
            arguments.Add("--filter-platform");
            arguments.Add(options.FilterPlatform.Trim());
        }

        if (options.AllFeatures)
        {
            arguments.Add("--all-features");
        }

        if (options.NoDefaultFeatures)
        {
            arguments.Add("--no-default-features");
        }

        if (options.Features.Count > 0)
        {
            arguments.Add("--features");
            arguments.Add(string.Join(",", options.Features));
        }

        arguments.AddRange(options.ExtraArguments);
        return arguments;
    }

    private bool FileExists(string relativePath) => File.Exists(Path.Combine(Directory, relativePath));

    private string DefaultBinPath(string name, string packageName)
    {
        foreach (var candidate in new[] { $"src/bin/{name}.rs", $"src/bin/{name}/main.rs" })
        {
            if (FileExists(candidate))
            {
                return candidate;
            }
        }

        return string.Equals(name, packageName, StringComparison.Ordinal) ? "src/main.rs" : $"src/bin/{name}.rs";
    }

    private void AddKind(List<CargoTargetDeclaration> result, IReadOnlyList<CargoTargetDeclaration> declared, bool auto, string folder, CargoTargetKind kind)
    {
        var discovered = auto ? Discover(folder, kind) : new List<CargoTargetDeclaration>();
        foreach (var target in declared)
        {
            var name = target.Name ?? string.Empty;
            var path = target.Path ?? discovered.FirstOrDefault(d => d.Name == name)?.Path ?? $"{folder}/{name}.rs";
            result.Add(Complete(target, name, path));
        }

        AddUndeclared(result, discovered, kind);
    }

    private static void AddUndeclared(List<CargoTargetDeclaration> result, List<CargoTargetDeclaration> discovered, CargoTargetKind kind)
    {
        foreach (var target in discovered)
        {
            var taken = result.Any(r => r.Kind == kind && (r.Name == target.Name || string.Equals(r.Path, target.Path, StringComparison.Ordinal)));
            if (!taken)
            {
                result.Add(target);
            }
        }
    }

    private List<CargoTargetDeclaration> Discover(string folder, CargoTargetKind kind)
    {
        var result = new List<CargoTargetDeclaration>();
        var directory = Path.Combine(Directory, folder);
        if (!System.IO.Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in System.IO.Directory.GetFiles(directory, "*.rs").OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Add(new CargoTargetDeclaration { Name = Path.GetFileNameWithoutExtension(file), Kind = kind, Path = $"{folder}/{Path.GetFileName(file)}" });
        }

        foreach (var sub in System.IO.Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(sub, "main.rs")))
            {
                var name = Path.GetFileName(sub);
                result.Add(new CargoTargetDeclaration { Name = name, Kind = kind, Path = $"{folder}/{name}/main.rs" });
            }
        }

        return result;
    }

    private static CargoTargetDeclaration Complete(CargoTargetDeclaration declared, string name, string path) => new CargoTargetDeclaration
    {
        Name = name,
        Kind = declared.Kind,
        Path = path,
        IsDeclared = true,
        RequiredFeatures = declared.RequiredFeatures,
        CrateTypes = declared.CrateTypes,
        Harness = declared.Harness,
        Test = declared.Test,
        Bench = declared.Bench,
        IsProcMacro = declared.IsProcMacro,
    };
}
