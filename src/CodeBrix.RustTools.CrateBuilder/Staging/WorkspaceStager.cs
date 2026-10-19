using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.Projects;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// The Staging stage: copies everything cargo needs to build the crate into ONE folder under
/// the run's scratch folder, so that a host build and a Docker copy-in see the same tree. The
/// resolved source is only read, never written.
/// </summary>
/// <remarks>
/// <para>Layout of the staging root <c>&lt;run folder&gt;/stage/&lt;package name&gt;/</c>
/// (<see cref="StagedWorkspace.Directory"/>):</para>
/// <list type="number">
/// <item><description><c>src/</c> - a copy of the crate's workspace root folder (found by
/// cargo's rule, see <see cref="WorkspaceRootFinder"/>; the crate's own folder when it is
/// standalone), Cargo.lock included;</description></item>
/// <item><description><c>.codebrix-vendor/&lt;n&gt;-&lt;name&gt;/</c> - one copy per path
/// dependency that lies OUTSIDE the copied tree: the dependency's own workspace root folder
/// when it belongs to a workspace (so its own inheritance keeps working), else the
/// dependency's folder; numbered from 1 in the order they are found.</description></item>
/// </list>
/// <para>Left out of every copy: <c>.git</c> at any depth, a <c>target</c> folder that is
/// cargo's output (beside a Cargo.toml, or holding CACHEDIR.TAG) at any depth, CrateBuilder's
/// own <c>.codebrix-*</c> folders at any depth, the version-control / CI / editor folders
/// directly below the copied root (<see cref="StagingFileSystem.ExcludedRootHiddenFolders"/>;
/// every other hidden folder, such as <c>.cargo</c> or <c>.sqlx</c>, is copied), and CrateBuilder's scratch root and
/// this run's folder when a consumer placed them inside the crate (a copy never copies
/// itself). Nothing else is filtered (no .gitignore rules).</para>
/// <para>Path-dependency closure: every Cargo.toml in the staged tree is read
/// (<c>[dependencies]</c>, <c>[dev-dependencies]</c>, <c>[build-dependencies]</c>, every
/// <c>[target.'...']</c> variant, <c>[workspace.dependencies]</c>, <c>[patch.*]</c>,
/// <c>[replace]</c>); a <c>path</c> that leaves its copied tree is pointed at the staged copy
/// (vendored first when needed, its own path dependencies followed in turn) by rewriting the
/// STAGED manifest through TomlParse's syntax tree, so comments and formatting survive. Every
/// rewrite is listed in <see cref="StagedWorkspace.PathRewrites"/>. A path that leads to no
/// Cargo.toml is left alone with a warning (cargo reports it if the build needs it), and a
/// manifest that is not valid TOML is skipped unless it is the crate's or the workspace
/// root's. Git dependencies are left to cargo and listed in
/// <see cref="StagedWorkspace.GitDependencies"/>.</para>
/// <para>The source hash (<see cref="SourceHasher"/>) is taken over the whole staging root
/// after the rewrites.</para>
/// </remarks>
internal sealed class WorkspaceStager : IWorkspaceStager
{
    /// <summary>The folder under the run folder that stagings go to.</summary>
    public const string StageFolderName = "stage";

    /// <summary>The folder (under the staging root) that holds the copied workspace.</summary>
    public const string SourceFolderName = "src";

    /// <summary>The folder (under the staging root) that holds vendored path
    /// dependencies.</summary>
    public const string VendorFolderName = ".codebrix-vendor";

    /// <summary>Creates the stager.</summary>
    public WorkspaceStager()
    {
    }

    /// <inheritdoc />
    public Task<StagedWorkspace> StageAsync(ResolvedSource source, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(() => Stage(source, context, cancellationToken), cancellationToken);
    }

    private static StagedWorkspace Stage(ResolvedSource source, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ManifestPath) || !File.Exists(source.ManifestPath))
        {
            throw new StagingException($"The resolved crate manifest '{source.ManifestPath}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(context.RunDirectory))
        {
            throw new StagingException("The run has no scratch folder to stage into.");
        }

        var crateManifest = Path.GetFullPath(source.ManifestPath);
        var boundary = source.IsClone ? source.RepositoryDirectory : null;
        CrateManifestFacts upstreamFacts;
        try
        {
            upstreamFacts = CrateManifestFacts.Read(crateManifest, null, boundary);
        }
        catch (InvalidDataException ex)
        {
            throw new StagingException(ex.Message, ex);
        }

        var rootManifest = upstreamFacts.WorkspaceRootManifestPath;
        var rootDirectory = Path.GetDirectoryName(rootManifest);
        var stageDirectory = Path.Combine(Path.GetFullPath(context.RunDirectory), StageFolderName, StagingFileSystem.SanitizeFolderName(upstreamFacts.PackageName, "crate"));
        var sourceDirectory = Path.Combine(stageDirectory, SourceFolderName);
        try
        {
            if (Directory.Exists(stageDirectory))
            {
                StagingFileSystem.DeleteDirectory(stageDirectory);
            }

            context.Report(CrateBuildStage.Staging, $"Copying the workspace {rootDirectory} to {sourceDirectory}.");
            var copied = StagingFileSystem.CopyTree(rootDirectory, sourceDirectory, cancellationToken, ExcludedFrom(context));
            context.Report(CrateBuildStage.Staging, $"Copied {copied} files.");

            var closure = new ClosureBuilder(stageDirectory, context, cancellationToken);
            closure.Run(new StagedUnit(rootDirectory, sourceDirectory), new[]
            {
                Path.Combine(sourceDirectory, StagingFileSystem.RelativePath(rootDirectory, crateManifest)),
                Path.Combine(sourceDirectory, CargoManifest.FileName),
            });

            var stagedManifest = Path.GetFullPath(Path.Combine(sourceDirectory, StagingFileSystem.RelativePath(rootDirectory, crateManifest)));
            var stagedRootManifest = Path.GetFullPath(Path.Combine(sourceDirectory, CargoManifest.FileName));
            var stagedFacts = CrateManifestFacts.Read(stagedManifest, stagedRootManifest);

            context.Report(CrateBuildStage.Staging, "Hashing the staged files.");
            var hash = SourceHasher.Compute(stageDirectory, cancellationToken);
            var identity = CopyIdentity(source.Identity);
            identity.CrateName = stagedFacts.PackageName;
            identity.CrateVersion = stagedFacts.Version;
            identity.CrateTypes = stagedFacts.CrateTypes;
            identity.SourceHash = hash;
            if (string.IsNullOrEmpty(identity.LocalManifestPath))
            {
                identity.LocalManifestPath = crateManifest;
            }

            return new StagedWorkspace
            {
                Directory = stageDirectory,
                WorkspaceManifestPath = stagedRootManifest,
                ManifestPath = stagedManifest,
                UpstreamManifestPath = stagedManifest,
                Identity = identity,
                IsFacade = false,
                CrateName = stagedFacts.LibraryName,
                PathRewrites = closure.Rewrites,
                GitDependencies = closure.GitDependencies,
            };
        }
        catch (InvalidDataException ex)
        {
            throw new StagingException(ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new StagingException($"Staging {crateManifest} into {stageDirectory} failed: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StagingException($"Staging {crateManifest} into {stageDirectory} failed: {ex.Message}", ex);
        }
    }

    /// <summary>The folders a staged copy leaves out besides the usual ones: CrateBuilder's
    /// scratch root and this run's folder (which a consumer may have placed inside the crate
    /// being staged - copying them would copy the copy).</summary>
    private static string[] ExcludedFrom(CrateBuilderContext context)
        => new[] { context.ScratchRootDirectory, context.RunDirectory };

    private static CrateSourceIdentity CopyIdentity(CrateSourceIdentity identity)
    {
        if (identity == null)
        {
            return new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind };
        }

        return new CrateSourceIdentity
        {
            Kind = identity.Kind,
            Url = identity.Url,
            Commit = identity.Commit,
            Branch = identity.Branch,
            Tag = identity.Tag,
            CloneReused = identity.CloneReused,
            ManifestPath = identity.ManifestPath,
            LocalManifestPath = identity.LocalManifestPath,
            SourceHash = identity.SourceHash,
            CrateName = identity.CrateName,
            CrateVersion = identity.CrateVersion,
            CrateTypes = identity.CrateTypes,
        };
    }

    private sealed class StagedUnit
    {
        public StagedUnit(string originalRoot, string stagedRoot)
        {
            OriginalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originalRoot));
            StagedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagedRoot));
        }

        public string OriginalRoot { get; }

        public string StagedRoot { get; }

        public string ToStaged(string originalPath) => Path.GetFullPath(Path.Combine(StagedRoot, StagingFileSystem.RelativePath(OriginalRoot, originalPath)));

        public string ToOriginal(string stagedPath) => Path.GetFullPath(Path.Combine(OriginalRoot, StagingFileSystem.RelativePath(StagedRoot, stagedPath)));
    }

    private sealed class ClosureBuilder
    {
        private readonly string _stageDirectory;
        private readonly CrateBuilderContext _context;
        private readonly CancellationToken _cancellationToken;
        private readonly List<StagedUnit> _units = new List<StagedUnit>();
        private readonly List<StagedPathRewrite> _rewrites = new List<StagedPathRewrite>();
        private readonly List<string> _gitDependencies = new List<string>();

        public ClosureBuilder(string stageDirectory, CrateBuilderContext context, CancellationToken cancellationToken)
        {
            _stageDirectory = stageDirectory;
            _context = context;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<StagedPathRewrite> Rewrites => _rewrites;

        public IReadOnlyList<string> GitDependencies => _gitDependencies;

        public void Run(StagedUnit main, IReadOnlyCollection<string> requiredManifests)
        {
            _units.Add(main);
            var required = new HashSet<string>(requiredManifests.Select(Path.GetFullPath), StagingFileSystem.PathComparer);
            for (var i = 0; i < _units.Count; i++)
            {
                foreach (var manifest in Manifests(_units[i].StagedRoot))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    Process(_units[i], manifest, required.Contains(manifest));
                }
            }
        }

        private static IEnumerable<string> Manifests(string root)
            => Directory.EnumerateFiles(root, CargoManifest.FileName, new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseSensitive })
                .Select(Path.GetFullPath)
                .OrderBy(p => StagingFileSystem.RelativePath(root, p), StringComparer.Ordinal)
                .ToList();

        private void Process(StagedUnit unit, string stagedManifest, bool required)
        {
            TomlTable document;
            try
            {
                document = WorkspaceRootFinder.ReadTable(stagedManifest);
            }
            catch (InvalidDataException)
            {
                if (required)
                {
                    throw;
                }

                return;
            }

            var stagedManifestDirectory = Path.GetDirectoryName(stagedManifest);
            var originalManifestDirectory = Path.GetDirectoryName(unit.ToOriginal(stagedManifest));
            var changes = new List<KeyValuePair<IReadOnlyList<string>, string>>();
            var records = new List<StagedPathRewrite>();
            foreach (var entry in ManifestDependencyScanner.Scan(document))
            {
                if (entry.Git != null)
                {
                    var description = ManifestDependencyScanner.DescribeGit(entry);
                    if (!_gitDependencies.Contains(description, StringComparer.Ordinal))
                    {
                        _gitDependencies.Add(description);
                    }
                }

                if (entry.Path == null)
                {
                    continue;
                }

                var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(originalManifestDirectory, entry.Path)));
                if (StagingFileSystem.IsSameOrInside(target, unit.OriginalRoot))
                {
                    continue;
                }

                var stagedTarget = ToStaged(target, stagedManifest, entry);
                if (stagedTarget == null)
                {
                    continue;
                }

                var newPath = StagingFileSystem.RelativePath(stagedManifestDirectory, stagedTarget);
                changes.Add(new KeyValuePair<IReadOnlyList<string>, string>(entry.TableKeys, newPath));
                records.Add(new StagedPathRewrite { ManifestPath = stagedManifest, DependencyName = entry.Name, OldPath = entry.Path, NewPath = newPath });
            }

            if (changes.Count == 0)
            {
                return;
            }

            var bytes = File.ReadAllBytes(stagedManifest);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            var rewritten = ManifestPathRewriter.Rewrite(text, changes, stagedManifest);
            File.WriteAllText(stagedManifest, rewritten, new UTF8Encoding(hasBom));
            foreach (var record in records)
            {
                _context.Report(CrateBuildStage.Staging, "Rewrote " + record + ".");
            }

            _rewrites.AddRange(records);
        }

        private string ToStaged(string target, string stagedManifest, ManifestDependencyEntry entry)
        {
            var owner = _units.FirstOrDefault(u => StagingFileSystem.IsSameOrInside(target, u.OriginalRoot));
            if (owner != null)
            {
                return owner.ToStaged(target);
            }

            if (!File.Exists(Path.Combine(target, CargoManifest.FileName)))
            {
                _context.Warnings.Add($"The path dependency '{entry.Name}' of {stagedManifest} points at '{target}', which holds no Cargo.toml; it was not staged.");
                return null;
            }

            var unitRoot = target;
            try
            {
                var rootManifest = WorkspaceRootFinder.FindRootManifest(Path.Combine(target, CargoManifest.FileName));
                unitRoot = Path.GetDirectoryName(rootManifest);
            }
            catch (InvalidDataException)
            {
                // Stage the crate's folder alone; cargo reports any problem with its workspace.
            }

            // The dependency's workspace root may CONTAIN the crate being built (a crate with
            // its own [workspace] nested inside another workspace, depending on a member of the
            // outer one): it is still vendored whole, because the dependency inherits from it
            // (edition.workspace = true, dep = { workspace = true }) and cargo finds that root
            // by walking up from the dependency. Only a root that overlaps an already VENDORED
            // copy falls back to the dependency's own folder, so no crate is staged twice.
            if (_units.Skip(1).Any(u => StagingFileSystem.IsSameOrInside(u.OriginalRoot, unitRoot) || StagingFileSystem.IsSameOrInside(unitRoot, u.OriginalRoot))
                || StagingFileSystem.IsSameOrInside(unitRoot, _units[0].OriginalRoot))
            {
                unitRoot = target;
            }

            var index = _units.Count;
            var name = StagingFileSystem.SanitizeFolderName(Path.GetFileName(Path.TrimEndingDirectorySeparator(unitRoot)), "crate");
            var stagedRoot = Path.Combine(_stageDirectory, VendorFolderName, index.ToString(CultureInfo.InvariantCulture) + "-" + name);
            _context.Report(CrateBuildStage.Staging, $"Vendoring the path dependency {unitRoot} to {stagedRoot}.");
            StagingFileSystem.CopyTree(unitRoot, stagedRoot, _cancellationToken, ExcludedFrom(_context));
            var unit = new StagedUnit(unitRoot, stagedRoot);
            _units.Add(unit);
            return unit.ToStaged(target);
        }
    }
}
