using System;
using System.IO;
using System.Linq;
using CodeBrix.LibGit2.Handlers;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// A crate in a Git repository, cloned with CodeBrix.LibGit2 (public HTTPS by default). When
/// <see cref="CloneDirectory"/> already holds a clone of the same remote it is fetched and
/// checked out again instead of cloned; a folder holding anything else is an error.
/// </summary>
public sealed class GitRepositorySource : CrateSource
{
    /// <summary>The default of <see cref="ManifestPath"/>: the Cargo.toml at the repository
    /// root.</summary>
    public const string DefaultManifestPath = "Cargo.toml";

    /// <summary>Creates an empty source; set <see cref="Url"/>.</summary>
    public GitRepositorySource()
    {
    }

    /// <summary>Creates a source for a repository URL.</summary>
    /// <param name="url">The repository's clone URL.</param>
    /// <param name="manifestPath">The Cargo.toml inside the repository, relative to its
    /// root.</param>
    public GitRepositorySource(string url, string manifestPath = DefaultManifestPath)
    {
        Url = url;
        ManifestPath = manifestPath;
    }

    /// <summary>The repository's clone URL, for example
    /// <c>https://github.com/owner/repo.git</c> (REQUIRED).</summary>
    public string Url { get; set; }

    /// <summary>The Cargo.toml to build, relative to the repository root (forward or back
    /// slashes; no "..", not rooted). Default <c>Cargo.toml</c>.</summary>
    public string ManifestPath { get; set; } = DefaultManifestPath;

    /// <summary>The commit to check out (a full or abbreviated SHA; detached HEAD). At most one
    /// of <see cref="Commit"/>, <see cref="Branch"/> and <see cref="Tag"/> may be set; none =
    /// the remote's default branch.</summary>
    public string Commit { get; set; }

    /// <summary>The branch to check out (see <see cref="Commit"/>).</summary>
    public string Branch { get; set; }

    /// <summary>The tag to check out (see <see cref="Commit"/>).</summary>
    public string Tag { get; set; }

    /// <summary>The folder to clone into; <c>null</c> = a folder CrateBuilder creates under
    /// its scratch root.</summary>
    public string CloneDirectory { get; set; }

    /// <summary>Delete the clone after the build: a clone CrateBuilder made in its scratch
    /// folder, or <see cref="CloneDirectory"/> when you set both. Default false.</summary>
    public bool CleanUpAfterBuild { get; set; }

    /// <summary>Clone and update the repository's submodules too. Default false.</summary>
    public bool RecurseSubmodules { get; set; }

    /// <summary>Passed to CodeBrix.LibGit2 for every network operation (clone, fetch) when
    /// the remote needs credentials; <c>null</c> = anonymous access.</summary>
    public CredentialsHandler CredentialsProvider { get; set; }

    /// <summary>"git: " plus the URL, the reference (when set) and the manifest path.</summary>
    public override string ToString()
    {
        var reference = Commit != null ? "@" + Commit : Branch != null ? "#" + Branch : Tag != null ? " tag " + Tag : string.Empty;
        return "git: " + Url + reference + " (" + ManifestPath + ")";
    }

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new ArgumentException("GitRepositorySource.Url is required: the repository's clone URL.");
        }

        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            throw new ArgumentException("GitRepositorySource.ManifestPath is required: the Cargo.toml to build, relative to the repository root.");
        }

        var manifest = ManifestPath.Trim();
        if (Path.IsPathRooted(manifest) || manifest.StartsWith('/') || manifest.StartsWith('\\'))
        {
            throw new ArgumentException($"GitRepositorySource.ManifestPath must be relative to the repository root, not rooted: '{ManifestPath}'.");
        }

        if (manifest.Split('/', '\\').Any(part => part == ".."))
        {
            throw new ArgumentException($"GitRepositorySource.ManifestPath must stay inside the repository (no '..'): '{ManifestPath}'.");
        }

        var references = new[] { Commit, Branch, Tag }.Count(r => !string.IsNullOrWhiteSpace(r));
        if (references > 1)
        {
            throw new ArgumentException("GitRepositorySource: set at most one of Commit, Branch and Tag.");
        }
    }

    internal override CrateSource Clone() => new GitRepositorySource
    {
        Url = Url,
        ManifestPath = ManifestPath,
        Commit = Commit,
        Branch = Branch,
        Tag = Tag,
        CloneDirectory = CloneDirectory,
        CleanUpAfterBuild = CleanUpAfterBuild,
        RecurseSubmodules = RecurseSubmodules,
        CredentialsProvider = CredentialsProvider,
    };
}
