using System;
using System.IO;
using System.Text;
using CodeBrix.LibGit2;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Sources;

/// <summary>
/// A local "upstream" Git repository made with CodeBrix.LibGit2 for the clone tests (file://
/// URLs need no network). History: on the default branch commit 1 (crate version 0.1.0,
/// annotated tag v0.1.0) and commit 2 (0.2.0); branch "feature" from commit 1 with commit 3
/// (0.3.0). Every commit holds a Cargo.toml for the crate "gitfix" at crate/Cargo.toml.
/// </summary>
internal sealed class GitTestRepository
{
    public const string ManifestPath = "crate/Cargo.toml";

    private static readonly Signature Author = new Signature("CrateBuilder Tests", "tests@example.invalid", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private GitTestRepository(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string Url => new Uri(Directory).AbsoluteUri;

    public string DefaultBranch { get; private set; }

    public string Commit1 { get; private set; }

    public string Commit2 { get; private set; }

    public string Commit3 { get; private set; }

    public static GitTestRepository Create(string directory)
    {
        var upstream = new GitTestRepository(directory);
        Repository.Init(directory);
        using var repository = new Repository(directory);
        upstream.Commit1 = upstream.CommitVersion(repository, "0.1.0", "first");
        repository.ApplyTag("v0.1.0", upstream.Commit1, Author, "Release 0.1.0");
        upstream.Commit2 = upstream.CommitVersion(repository, "0.2.0", "second");
        upstream.DefaultBranch = repository.Head.FriendlyName;
        var feature = repository.CreateBranch("feature", upstream.Commit1);
        Commands.Checkout(repository, feature);
        upstream.Commit3 = upstream.CommitVersion(repository, "0.3.0", "feature work");
        Commands.Checkout(repository, repository.Branches[upstream.DefaultBranch]);
        return upstream;
    }

    /// <summary>Adds a commit with the given crate version on <paramref name="branch"/> (the
    /// default branch when <c>null</c>) and returns its SHA; the default branch stays checked
    /// out.</summary>
    public string AddCommit(string version, string branch = null)
    {
        using var repository = new Repository(Directory);
        var name = branch ?? DefaultBranch;
        Commands.Checkout(repository, repository.Branches[name]);
        var sha = CommitVersion(repository, version, "version " + version);
        Commands.Checkout(repository, repository.Branches[DefaultBranch]);
        return sha;
    }

    /// <summary>Adds a commit on the default branch that records the default-branch tip of
    /// <paramref name="submodule"/> as the submodule <paramref name="path"/> (a gitlink plus a
    /// <c>.gitmodules</c> entry with the submodule's file:// URL) and returns its SHA. The
    /// upstream's own working tree is left as it is (clones read the commit).</summary>
    public string AddSubmoduleCommit(string path, GitTestRepository submodule)
    {
        using var repository = new Repository(Directory);
        using var sub = new Repository(submodule.Directory);
        var parent = repository.Branches[DefaultBranch].Tip;
        var definition = TreeDefinition.From(parent);
        var gitmodules = "[submodule \"" + path + "\"]\n\tpath = " + path + "\n\turl = " + submodule.Url + "\n";
        var blob = repository.ObjectDatabase.CreateBlob(new MemoryStream(Encoding.UTF8.GetBytes(gitmodules)));
        definition.Add(".gitmodules", blob, Mode.NonExecutableFile);
        definition.AddGitLink(path, sub.Branches[submodule.DefaultBranch].Tip.Id);
        var tree = repository.ObjectDatabase.CreateTree(definition);
        var commit = repository.ObjectDatabase.CreateCommit(Author, Author, "add submodule " + path, tree, new[] { parent }, false);
        repository.Refs.UpdateTarget(repository.Branches[DefaultBranch].CanonicalName, commit.Sha);
        return commit.Sha;
    }

    private string CommitVersion(Repository repository, string version, string message)
    {
        var crate = Path.Combine(Directory, "crate");
        System.IO.Directory.CreateDirectory(Path.Combine(crate, "src"));
        File.WriteAllText(Path.Combine(crate, "Cargo.toml"), "[package]\nname = \"gitfix\"\nversion = \"" + version + "\"\nedition = \"2024\"\n\n[lib]\ncrate-type = [\"cdylib\"]\n");
        File.WriteAllText(Path.Combine(crate, "src", "lib.rs"), "pub fn version() -> &'static str { \"" + version + "\" }\n");
        Commands.Stage(repository, "*");
        return repository.Commit(message, Author, Author).Sha;
    }
}
