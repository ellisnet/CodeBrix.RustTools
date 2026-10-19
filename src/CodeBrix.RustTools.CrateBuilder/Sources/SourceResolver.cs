using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.LibGit2;
using CodeBrix.LibGit2.Handlers;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.CrateBuilder.Sources;

/// <summary>
/// The ResolvingSource stage. A <see cref="LocalManifestSource"/> is used where it is (only
/// read). A <see cref="GitRepositorySource"/> is cloned with CodeBrix.LibGit2 into
/// <see cref="GitRepositorySource.CloneDirectory"/> (or, when that is <c>null</c>, into
/// <c>clone/&lt;repository name&gt;</c> under the run's scratch folder) and checked out at the
/// requested commit (detached HEAD), branch (a local branch reset to <c>origin/&lt;branch&gt;</c>)
/// or tag (detached at the tag's commit).
/// </summary>
/// <remarks>
/// <para>Reuse rule: a clone folder that already holds a non-bare clone whose <c>origin</c>
/// remote is the requested URL (trailing "/" and ".git" ignored, host case-insensitive) is
/// fetched (with the credentials callback, tags included) and checked out again instead of
/// cloned. With no commit, branch or tag requested, a reused clone is checked out DETACHED at
/// the tip of the remote's default branch (the remote's HEAD, read from the remote, else
/// <c>refs/remotes/origin/HEAD</c>); a fresh clone stays on the branch the clone checked out.
/// Checkouts are safe checkouts: local changes that a checkout would overwrite fail the stage
/// instead of being discarded. A folder holding anything else (files that are not a Git
/// repository, a bare repository, a clone of another remote, a repository without an origin
/// remote) is an error naming the folder and what was found.</para>
/// <para>Clean-up: when <see cref="GitRepositorySource.CleanUpAfterBuild"/> is true the clone
/// folder is deleted in the CleaningUp stage - a clone CrateBuilder made, and equally the
/// consumer's own <see cref="GitRepositorySource.CloneDirectory"/>, also when it held a
/// clone from before this run that was reused, because that is what the consumer asked for.
/// When it is false nothing is deleted by this stage (a default clone folder under the run
/// folder goes with the run folder unless the options keep scratch). A clone this stage
/// created is removed again when the stage itself fails.</para>
/// </remarks>
internal sealed class SourceResolver : ISourceResolver
{
    /// <summary>The name of the remote a clone records.</summary>
    public const string OriginRemoteName = "origin";

    /// <summary>The folder under the run folder that default clones go to.</summary>
    public const string CloneFolderName = "clone";

    private static readonly TimeSpan TransferReportInterval = TimeSpan.FromSeconds(2);

    /// <summary>Creates the resolver.</summary>
    public SourceResolver()
    {
    }

    /// <inheritdoc />
    public Task<ResolvedSource> ResolveAsync(CrateSource source, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        switch (source)
        {
            case LocalManifestSource local:
                try
                {
                    return Task.FromResult(ResolveLocal(local, context));
                }
                catch (Exception ex)
                {
                    return Task.FromException<ResolvedSource>(ex);
                }

            case GitRepositorySource git:
                return Task.Run(() => ResolveGit(git, context, cancellationToken), cancellationToken);
            default:
                return Task.FromException<ResolvedSource>(new SourceResolutionException($"Unsupported source type {source.GetType().Name}."));
        }
    }

    /// <summary>The folder a clone goes to when <see cref="GitRepositorySource.CloneDirectory"/>
    /// is <c>null</c>: <c>&lt;run folder&gt;/clone/&lt;repository name&gt;</c>.</summary>
    public static string DefaultCloneDirectory(string runDirectory, string url)
        => Path.Combine(runDirectory, CloneFolderName, GitRemoteUrl.RepositoryName(url));

    private static ResolvedSource ResolveLocal(LocalManifestSource source, CrateBuilderContext context)
    {
        if (string.IsNullOrWhiteSpace(source.ManifestPath))
        {
            throw new SourceResolutionException("LocalManifestSource.ManifestPath is required: the path of a Cargo.toml or of its folder.");
        }

        var asked = Path.GetFullPath(source.ManifestPath.Trim());
        string manifest;
        if (Directory.Exists(asked))
        {
            manifest = Path.Combine(asked, CargoManifest.FileName);
        }
        else if (File.Exists(asked))
        {
            if (!StagingFileSystem.PathComparer.Equals(Path.GetFileName(asked), CargoManifest.FileName))
            {
                throw new SourceResolutionException($"'{asked}' is not a Cargo.toml: name the crate's Cargo.toml or the folder that holds it.");
            }

            manifest = asked;
        }
        else
        {
            manifest = asked;
        }

        if (!File.Exists(manifest))
        {
            throw new SourceResolutionException(
                $"No Cargo.toml was found at '{asked}'.",
                new FileNotFoundException("The crate's Cargo.toml does not exist.", manifest));
        }

        context.Report(CrateBuildStage.ResolvingSource, "Using the local crate " + manifest + ".");
        var facts = ReadFacts(manifest, null);
        return new ResolvedSource
        {
            RepositoryDirectory = Path.GetDirectoryName(facts.WorkspaceRootManifestPath),
            ManifestPath = manifest,
            IsClone = false,
            CleanUp = null,
            Identity = new CrateSourceIdentity
            {
                Kind = CrateSourceIdentity.LocalKind,
                ManifestPath = asked,
                LocalManifestPath = manifest,
                CrateName = facts.PackageName,
                CrateVersion = facts.Version,
                CrateTypes = facts.CrateTypes,
            },
        };
    }

    private static ResolvedSource ResolveGit(GitRepositorySource source, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
        {
            throw new SourceResolutionException("GitRepositorySource.Url is required: the repository's clone URL.");
        }

        var references = new[] { source.Commit, source.Branch, source.Tag }.Count(r => !string.IsNullOrWhiteSpace(r));
        if (references > 1)
        {
            throw new ArgumentException("GitRepositorySource: set at most one of Commit, Branch and Tag.");
        }

        var manifestRelative = (string.IsNullOrWhiteSpace(source.ManifestPath) ? GitRepositorySource.DefaultManifestPath : source.ManifestPath.Trim()).Replace('\\', '/');
        if (Path.IsPathRooted(manifestRelative) || manifestRelative.StartsWith('/') || manifestRelative.Split('/').Any(p => p == ".."))
        {
            throw new SourceResolutionException($"GitRepositorySource.ManifestPath must be a path inside the repository: '{source.ManifestPath}'.");
        }

        string cloneDirectory;
        if (!string.IsNullOrWhiteSpace(source.CloneDirectory))
        {
            cloneDirectory = Path.GetFullPath(source.CloneDirectory.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(context.RunDirectory))
        {
            cloneDirectory = DefaultCloneDirectory(context.RunDirectory, source.Url);
        }
        else
        {
            throw new SourceResolutionException("No clone folder: GitRepositorySource.CloneDirectory is not set and the run has no scratch folder.");
        }

        cloneDirectory = Path.TrimEndingDirectorySeparator(cloneDirectory);
        var existed = Directory.Exists(cloneDirectory);
        var reuse = existed && !StagingFileSystem.IsEmptyDirectory(cloneDirectory);
        var credentials = source.CredentialsProvider;
        try
        {
            if (reuse)
            {
                CheckReusable(cloneDirectory, source.Url);
            }

            string commitSha;
            if (!reuse)
            {
                Clone(source, cloneDirectory, context, cancellationToken);
            }

            using (var repository = new Repository(cloneDirectory))
            {
                if (reuse)
                {
                    Fetch(repository, source.Url, credentials, context, cancellationToken);
                }

                Checkout(repository, source, reuse, credentials, context);
                cancellationToken.ThrowIfCancellationRequested();
                if (source.RecurseSubmodules)
                {
                    UpdateSubmodules(repository, credentials, context, cancellationToken);
                }

                commitSha = repository.Head?.Tip?.Sha
                    ?? throw new SourceResolutionException($"The clone in '{cloneDirectory}' has no commit checked out.");
            }

            var manifest = Path.GetFullPath(Path.Combine(cloneDirectory, manifestRelative));
            if (!StagingFileSystem.IsSameOrInside(manifest, cloneDirectory))
            {
                throw new SourceResolutionException($"GitRepositorySource.ManifestPath '{source.ManifestPath}' leads outside the clone.");
            }

            if (Directory.Exists(manifest))
            {
                manifest = Path.Combine(manifest, CargoManifest.FileName);
            }

            if (!File.Exists(manifest))
            {
                throw new SourceResolutionException(
                    $"The repository {source.Url} has no '{source.ManifestPath}' at commit {commitSha} (looked for {manifest}).",
                    new FileNotFoundException("The crate's Cargo.toml does not exist in the clone.", manifest));
            }

            var facts = ReadFacts(manifest, cloneDirectory);
            context.Report(CrateBuildStage.ResolvingSource, $"Checked out {source.Url} at {commitSha}.");
            return new ResolvedSource
            {
                RepositoryDirectory = cloneDirectory,
                ManifestPath = manifest,
                IsClone = true,
                CleanUp = source.CleanUpAfterBuild ? () => StagingFileSystem.DeleteDirectory(cloneDirectory) : null,
                Identity = new CrateSourceIdentity
                {
                    Kind = CrateSourceIdentity.GitKind,
                    Url = source.Url,
                    Commit = commitSha,
                    Branch = string.IsNullOrWhiteSpace(source.Branch) ? null : source.Branch,
                    Tag = string.IsNullOrWhiteSpace(source.Tag) ? null : source.Tag,
                    CloneReused = reuse,
                    ManifestPath = source.ManifestPath,
                    LocalManifestPath = manifest,
                    CrateName = facts.PackageName,
                    CrateVersion = facts.Version,
                    CrateTypes = facts.CrateTypes,
                },
            };
        }
        catch (Exception ex) when (ex is not SourceResolutionException && ex is not OperationCanceledException && ex is not ArgumentException)
        {
            RemoveFailedClone(cloneDirectory, existed, reuse);
            if (ex is UserCancelledException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The clone or fetch was cancelled.", ex, cancellationToken);
            }

            throw new SourceResolutionException($"Resolving {source.Url} in '{cloneDirectory}' failed: {ex.Message}", ex);
        }
        catch
        {
            RemoveFailedClone(cloneDirectory, existed, reuse);
            throw;
        }
    }

    private static void CheckReusable(string cloneDirectory, string url)
    {
        if (!Repository.IsValid(cloneDirectory))
        {
            throw new SourceResolutionException(
                $"The clone folder '{cloneDirectory}' is not empty and is not a Git repository; it cannot be cloned into or reused. Use an empty or absent folder.");
        }

        using var repository = new Repository(cloneDirectory);
        if (repository.Info.IsBare || repository.Info.WorkingDirectory == null)
        {
            throw new SourceResolutionException($"The clone folder '{cloneDirectory}' holds a bare Git repository; a clone with a working tree is needed.");
        }

        if (!StagingFileSystem.PathEquals(repository.Info.WorkingDirectory, cloneDirectory))
        {
            throw new SourceResolutionException(
                $"The clone folder '{cloneDirectory}' is not the root of a working tree (the repository's working tree is '{repository.Info.WorkingDirectory}').");
        }

        var origin = repository.Network.Remotes[OriginRemoteName];
        if (origin == null)
        {
            throw new SourceResolutionException($"The clone folder '{cloneDirectory}' holds a Git repository without an '{OriginRemoteName}' remote, not a clone of {url}.");
        }

        if (!GitRemoteUrl.AreSame(origin.Url, url))
        {
            throw new SourceResolutionException($"The clone folder '{cloneDirectory}' holds a clone of {origin.Url}, not of {url}.");
        }
    }

    private static void Clone(GitRepositorySource source, string cloneDirectory, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cloneDirectory));
        context.Report(CrateBuildStage.ResolvingSource, $"Cloning {source.Url} into {cloneDirectory}.");
        var options = new CloneOptions { RecurseSubmodules = source.RecurseSubmodules };
        options.FetchOptions.CredentialsProvider = source.CredentialsProvider;
        options.FetchOptions.OnTransferProgress = TransferProgress(context, cancellationToken);
        Repository.Clone(source.Url, cloneDirectory, options);
    }

    private static void Fetch(Repository repository, string url, CredentialsHandler credentials, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        context.Report(CrateBuildStage.ResolvingSource, $"Reusing the clone in {repository.Info.WorkingDirectory}; fetching {url}.");
        var origin = repository.Network.Remotes[OriginRemoteName];
        var options = new FetchOptions
        {
            CredentialsProvider = credentials,
            TagFetchMode = TagFetchMode.All,
            OnTransferProgress = TransferProgress(context, cancellationToken),
        };
        Commands.Fetch(repository, OriginRemoteName, origin.FetchRefSpecs.Select(r => r.Specification).ToList(), options, "CodeBrix.RustTools.CrateBuilder fetch");
    }

    private static void Checkout(Repository repository, GitRepositorySource source, bool reused, CredentialsHandler credentials, CrateBuilderContext context)
    {
        if (!string.IsNullOrWhiteSpace(source.Commit))
        {
            var commit = LookupCommit(repository, source.Commit.Trim())
                ?? throw new SourceResolutionException($"The commit '{source.Commit}' does not exist in {source.Url}.");
            context.Report(CrateBuildStage.ResolvingSource, $"Checking out commit {commit.Sha} (detached HEAD).");
            Commands.Checkout(repository, commit);
            return;
        }

        if (!string.IsNullOrWhiteSpace(source.Branch))
        {
            var name = source.Branch.Trim();
            var remoteBranch = repository.Branches[OriginRemoteName + "/" + name]
                ?? throw new SourceResolutionException(
                    $"The branch '{name}' does not exist in {source.Url}. Branches: " + string.Join(", ", RemoteBranchNames(repository)) + ".");
            var tip = remoteBranch.Tip;
            var head = repository.Head;
            if (!repository.Info.IsHeadDetached && head != null && string.Equals(head.FriendlyName, name, StringComparison.Ordinal))
            {
                // A branch cannot be overwritten while HEAD points at it: detach first.
                Commands.Checkout(repository, tip);
            }

            var local = repository.Branches.Add(name, tip, allowOverwrite: true);
            local = repository.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
            context.Report(CrateBuildStage.ResolvingSource, $"Checking out branch {name} at {tip.Sha}.");
            Commands.Checkout(repository, local);
            return;
        }

        if (!string.IsNullOrWhiteSpace(source.Tag))
        {
            var name = source.Tag.Trim();
            var tag = repository.Tags[name]
                ?? throw new SourceResolutionException($"The tag '{name}' does not exist in {source.Url}.");
            var commit = (tag.PeeledTarget ?? tag.Target) as Commit
                ?? throw new SourceResolutionException($"The tag '{name}' of {source.Url} does not point at a commit.");
            context.Report(CrateBuildStage.ResolvingSource, $"Checking out tag {name} at {commit.Sha} (detached HEAD).");
            Commands.Checkout(repository, commit);
            return;
        }

        if (reused)
        {
            var defaultBranch = RemoteDefaultBranch(repository, credentials);
            var remoteBranch = defaultBranch == null ? null : repository.Branches[OriginRemoteName + "/" + defaultBranch];
            if (remoteBranch?.Tip != null)
            {
                context.Report(CrateBuildStage.ResolvingSource, $"Checking out {OriginRemoteName}/{defaultBranch} at {remoteBranch.Tip.Sha} (detached HEAD).");
                Commands.Checkout(repository, remoteBranch.Tip);
            }
            else
            {
                context.Report(CrateBuildStage.ResolvingSource, "The remote's default branch is unknown; keeping the clone's current checkout.");
            }
        }
    }

    private static Commit LookupCommit(Repository repository, string committish)
    {
        try
        {
            return repository.Lookup<Commit>(committish);
        }
        catch (LibGit2SharpException ex)
        {
            throw new SourceResolutionException($"The commit '{committish}' cannot be resolved: {ex.Message}", ex);
        }
    }

    private static IEnumerable<string> RemoteBranchNames(Repository repository)
        => repository.Branches.Where(b => b.IsRemote && b.RemoteName == OriginRemoteName)
            .Select(b => b.FriendlyName.Substring(OriginRemoteName.Length + 1))
            .Where(n => n != "HEAD")
            .OrderBy(n => n, StringComparer.Ordinal);

    private static string RemoteDefaultBranch(Repository repository, CredentialsHandler credentials)
    {
        const string headsPrefix = "refs/heads/";
        const string remotePrefix = "refs/remotes/" + OriginRemoteName + "/";
        try
        {
            var origin = repository.Network.Remotes[OriginRemoteName];
            var references = repository.Network.ListReferences(origin, credentials);
            var head = references.FirstOrDefault(r => r.CanonicalName == "HEAD");
            var target = (head as SymbolicReference)?.TargetIdentifier;
            if (target != null && target.StartsWith(headsPrefix, StringComparison.Ordinal))
            {
                return target.Substring(headsPrefix.Length);
            }
        }
        catch (LibGit2SharpException)
        {
            // Fall back to what the clone recorded.
        }

        var local = repository.Refs[remotePrefix + "HEAD"] as SymbolicReference;
        var localTarget = local?.TargetIdentifier;
        return localTarget != null && localTarget.StartsWith(remotePrefix, StringComparison.Ordinal) ? localTarget.Substring(remotePrefix.Length) : null;
    }

    private static void UpdateSubmodules(Repository repository, CredentialsHandler credentials, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        foreach (var submodule in repository.Submodules.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Report(CrateBuildStage.ResolvingSource, $"Updating submodule {submodule.Path}.");
            var options = new SubmoduleUpdateOptions { Init = true };
            options.FetchOptions.CredentialsProvider = credentials;
            options.FetchOptions.OnTransferProgress = TransferProgress(context, cancellationToken);
            repository.Submodules.Update(submodule.Name, options);
            var path = Path.Combine(repository.Info.WorkingDirectory, submodule.Path);
            if (Repository.IsValid(path))
            {
                using var child = new Repository(path);
                UpdateSubmodules(child, credentials, context, cancellationToken);
            }
        }
    }

    private static TransferProgressHandler TransferProgress(CrateBuilderContext context, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        return progress =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (clock.Elapsed - last >= TransferReportInterval)
            {
                last = clock.Elapsed;
                context.Report(CrateBuildStage.ResolvingSource, $"Received {progress.ReceivedObjects} of {progress.TotalObjects} objects ({progress.ReceivedBytes / 1024} KiB).");
            }

            return true;
        };
    }

    private static CrateManifestFacts ReadFacts(string manifest, string boundaryDirectory)
    {
        try
        {
            return CrateManifestFacts.Read(manifest, null, boundaryDirectory);
        }
        catch (InvalidDataException ex)
        {
            throw new SourceResolutionException(ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new SourceResolutionException($"{manifest} cannot be read: {ex.Message}", ex);
        }
    }

    private static void RemoveFailedClone(string cloneDirectory, bool existed, bool reused)
    {
        if (reused)
        {
            return;
        }

        try
        {
            if (existed)
            {
                StagingFileSystem.DeleteContents(cloneDirectory);
            }
            else
            {
                StagingFileSystem.DeleteDirectory(cloneDirectory);
            }
        }
        catch (IOException)
        {
            // Best effort: the stage's own error is the one to report.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
