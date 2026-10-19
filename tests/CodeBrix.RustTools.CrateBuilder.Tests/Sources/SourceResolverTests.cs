using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using CodeBrix.LibGit2;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.Staging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Sources;

public class SourceResolverTests
{
    private static (GitTestRepository Upstream, string RunDirectory) Upstream(SampleSandbox sandbox)
        => (GitTestRepository.Create(sandbox.CreateDirectory("upstream")), Path.Combine(sandbox.Directory, "run"));

    // ---- local sources ----

    [Fact]
    public async Task local_manifest_path_resolves_with_inherited_version_and_crate_types()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var manifest = Path.Combine(sandbox.Directory, "crates", "alpha", "Cargo.toml");
        var context = StagingContexts.Create(Path.Combine(sandbox.Directory, "..", "run"));

        //Act
        var resolved = await new SourceResolver().ResolveAsync(new LocalManifestSource(manifest), context, TestContext.Current.CancellationToken);

        //Assert
        resolved.ManifestPath.Should().Be(manifest);
        resolved.RepositoryDirectory.Should().Be(sandbox.Directory);
        resolved.IsClone.Should().BeFalse();
        resolved.CleanUp.Should().BeNull();
        resolved.Identity.Kind.Should().Be(CrateSourceIdentity.LocalKind);
        resolved.Identity.ManifestPath.Should().Be(manifest);
        resolved.Identity.LocalManifestPath.Should().Be(manifest);
        resolved.Identity.CrateName.Should().Be("alpha");
        resolved.Identity.CrateVersion.Should().Be("0.18.2");
        resolved.Identity.CrateTypes.Should().Equal("rlib");
        resolved.Identity.SourceHash.Should().BeNull();
        resolved.Identity.CloneReused.Should().BeFalse("a local source is never a reused clone");
    }

    [Fact]
    public async Task local_folder_resolves_to_its_cargo_toml()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var folder = Path.Combine(sandbox.Directory, "external", "gamma");
        var context = StagingContexts.Create(Path.Combine(sandbox.Directory, "..", "run"));

        //Act
        var resolved = await new SourceResolver().ResolveAsync(new LocalManifestSource(folder), context, TestContext.Current.CancellationToken);

        //Assert
        resolved.ManifestPath.Should().Be(Path.Combine(folder, "Cargo.toml"));
        resolved.RepositoryDirectory.Should().Be(folder, "gamma is excluded from the workspace, so it is standalone");
        resolved.Identity.ManifestPath.Should().Be(folder);
        resolved.Identity.CrateVersion.Should().Be("1.2.0");
        resolved.Identity.CrateTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task local_missing_manifest_is_a_source_resolution_exception()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = StagingContexts.Create(Path.Combine(sandbox.Directory, "run"));
        Func<Task> act = () => new SourceResolver().ResolveAsync(new LocalManifestSource(Path.Combine(sandbox.Directory, "nothing-here")), context, TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.Message.Should().Contain("No Cargo.toml was found");
        exception.InnerException.Should().BeOfType<FileNotFoundException>();
        exception.Stage.Should().Be(CrateBuildStage.ResolvingSource);
    }

    [Fact]
    public async Task local_file_that_is_not_a_cargo_toml_is_rejected()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var file = sandbox.WriteFile("notes.toml", "a = 1\n");
        var context = StagingContexts.Create(Path.Combine(sandbox.Directory, "run"));
        Func<Task> act = () => new SourceResolver().ResolveAsync(new LocalManifestSource(file), context, TestContext.Current.CancellationToken);

        //Act + Assert
        (await act.Should().ThrowAsync<SourceResolutionException>()).Which.Message.Should().Contain("is not a Cargo.toml");
    }

    [Fact]
    public async Task local_virtual_workspace_manifest_is_rejected()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var context = StagingContexts.Create(Path.Combine(sandbox.Directory, "..", "run"));
        Func<Task> act = () => new SourceResolver().ResolveAsync(new LocalManifestSource(sandbox.Directory), context, TestContext.Current.CancellationToken);

        //Act + Assert
        (await act.Should().ThrowAsync<SourceResolutionException>()).Which.Message.Should().Contain("virtual workspace manifest");
    }

    // ---- git sources (local upstream through file:// URLs) ----

    [Fact]
    public async Task git_default_clone_goes_to_the_run_folder_and_stays_on_the_default_branch()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var reports = new List<CrateBuildProgress>();
        var context = StagingContexts.Create(run, reports);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath);

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, context, TestContext.Current.CancellationToken);

        //Assert
        resolved.RepositoryDirectory.Should().Be(Path.Combine(run, "clone", "upstream"));
        resolved.IsClone.Should().BeTrue();
        resolved.CleanUp.Should().BeNull();
        resolved.ManifestPath.Should().Be(Path.Combine(resolved.RepositoryDirectory, "crate", "Cargo.toml"));
        resolved.Identity.Kind.Should().Be(CrateSourceIdentity.GitKind);
        resolved.Identity.Url.Should().Be(upstream.Url);
        resolved.Identity.Commit.Should().Be(upstream.Commit2);
        resolved.Identity.Branch.Should().BeNull();
        resolved.Identity.Tag.Should().BeNull();
        resolved.Identity.ManifestPath.Should().Be(GitTestRepository.ManifestPath);
        resolved.Identity.LocalManifestPath.Should().Be(resolved.ManifestPath);
        resolved.Identity.CrateName.Should().Be("gitfix");
        resolved.Identity.CrateVersion.Should().Be("0.2.0");
        resolved.Identity.CrateTypes.Should().Equal("cdylib");
        resolved.Identity.CloneReused.Should().BeFalse("a fresh clone");
        using (var clone = new Repository(resolved.RepositoryDirectory))
        {
            clone.Info.IsHeadDetached.Should().BeFalse();
            clone.Head.FriendlyName.Should().Be(upstream.DefaultBranch);
        }

        reports.Should().Contain(r => r.Stage == CrateBuildStage.ResolvingSource && r.Message.StartsWith("Cloning "));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task git_submodules_are_checked_out_only_with_RecurseSubmodules(bool recurse)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var submodule = GitTestRepository.Create(sandbox.CreateDirectory("submodule"));
        var withSubmodule = upstream.AddSubmoduleCommit("vendor/sub", submodule);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { RecurseSubmodules = recurse };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.Identity.Commit.Should().Be(withSubmodule);
        var submoduleManifest = Path.Combine(resolved.RepositoryDirectory, "vendor", "sub", "crate", "Cargo.toml");
        File.Exists(submoduleManifest).Should().Be(recurse);
        if (recurse)
        {
            File.ReadAllText(submoduleManifest).Should().Contain("version = \"0.2.0\"");
            using var clone = new Repository(resolved.RepositoryDirectory);
            clone.Submodules["vendor/sub"].WorkDirCommitId.Sha.Should().Be(submodule.Commit2);
        }
    }

    [Fact]
    public async Task git_reuse_with_RecurseSubmodules_checks_out_a_submodule_added_upstream_later()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var submodule = GitTestRepository.Create(sandbox.CreateDirectory("submodule"));
        var cloneDirectory = Path.Combine(sandbox.Directory, "clone");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory, RecurseSubmodules = true };
        await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);
        upstream.AddSubmoduleCommit("vendor/sub", submodule);

        //Act
        var reused = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(Path.Combine(sandbox.Directory, "run2")), TestContext.Current.CancellationToken);

        //Assert
        reused.RepositoryDirectory.Should().Be(cloneDirectory);
        File.Exists(Path.Combine(cloneDirectory, "vendor", "sub", "crate", "Cargo.toml")).Should().BeTrue();
    }

    [Fact]
    public async Task git_commit_is_checked_out_detached_from_an_abbreviated_sha()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { Commit = upstream.Commit1.Substring(0, 10) };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.Identity.Commit.Should().Be(upstream.Commit1);
        resolved.Identity.CrateVersion.Should().Be("0.1.0");
        using var clone = new Repository(resolved.RepositoryDirectory);
        clone.Info.IsHeadDetached.Should().BeTrue();
    }

    [Fact]
    public async Task git_branch_is_checked_out_as_a_local_branch_tracking_origin()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { Branch = "feature" };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.Identity.Commit.Should().Be(upstream.Commit3);
        resolved.Identity.Branch.Should().Be("feature");
        resolved.Identity.CrateVersion.Should().Be("0.3.0");
        using var clone = new Repository(resolved.RepositoryDirectory);
        clone.Info.IsHeadDetached.Should().BeFalse();
        clone.Head.FriendlyName.Should().Be("feature");
        clone.Head.TrackedBranch.CanonicalName.Should().Be("refs/remotes/origin/feature");
    }

    [Fact]
    public async Task git_tag_is_checked_out_at_its_commit()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { Tag = "v0.1.0" };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.Identity.Commit.Should().Be(upstream.Commit1);
        resolved.Identity.Tag.Should().Be("v0.1.0");
        resolved.Identity.CrateVersion.Should().Be("0.1.0");
    }

    [Fact]
    public async Task git_reuse_of_the_same_remote_fetches_and_checks_out_the_new_default_tip()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var cloneDirectory = Path.Combine(sandbox.Directory, "my-clone");
        var first = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory };
        var firstResolved = await new SourceResolver().ResolveAsync(first, StagingContexts.Create(run), TestContext.Current.CancellationToken);
        var marker = Path.Combine(cloneDirectory, "untracked-marker.txt");
        File.WriteAllText(marker, "proves the folder was reused, not re-cloned");
        var commit4 = upstream.AddCommit("0.4.0");
        var reports = new List<CrateBuildProgress>();
        var second = new GitRepositorySource(upstream.Url.TrimEnd('/') + ".git/", GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(second, StagingContexts.Create(run, reports), TestContext.Current.CancellationToken);

        //Assert
        firstResolved.Identity.CloneReused.Should().BeFalse("the first resolve cloned into the empty folder");
        resolved.Identity.CloneReused.Should().BeTrue("the second resolve fetched into the existing clone");
        resolved.Identity.Commit.Should().Be(commit4);
        resolved.Identity.CrateVersion.Should().Be("0.4.0");
        File.Exists(marker).Should().BeTrue();
        reports.Should().Contain(r => r.Message.StartsWith("Reusing the clone"));
        reports.Should().NotContain(r => r.Message.StartsWith("Cloning "));
        using var clone = new Repository(cloneDirectory);
        clone.Info.IsHeadDetached.Should().BeTrue();
    }

    [Fact]
    public async Task git_reuse_updates_the_checked_out_branch_to_the_fetched_tip()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var cloneDirectory = Path.Combine(sandbox.Directory, "my-clone");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory, Branch = "feature" };
        await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);
        var commit5 = upstream.AddCommit("0.5.0", "feature");

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.Identity.Commit.Should().Be(commit5);
        using var clone = new Repository(cloneDirectory);
        clone.Head.FriendlyName.Should().Be("feature");
        clone.Head.Tip.Sha.Should().Be(commit5);
    }

    [Fact]
    public async Task git_folder_holding_a_clone_of_another_remote_is_rejected_and_left_alone()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var other = GitTestRepository.Create(sandbox.CreateDirectory("other"));
        var cloneDirectory = Path.Combine(sandbox.Directory, "my-clone");
        Repository.Clone(other.Url, cloneDirectory);
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory, CleanUpAfterBuild = true };
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.Message.Should().Contain(cloneDirectory).And.Contain("holds a clone of " + other.Url);
        File.Exists(Path.Combine(cloneDirectory, "crate", "Cargo.toml")).Should().BeTrue();
    }

    [Fact]
    public async Task git_folder_holding_files_that_are_not_a_repository_is_rejected_and_left_alone()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var file = sandbox.WriteFile(Path.Combine("busy", "keep.txt"), "mine");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = Path.GetDirectoryName(file) };
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.Message.Should().Contain("is not a Git repository");
        File.ReadAllText(file).Should().Be("mine");
    }

    [Fact]
    public async Task git_empty_existing_folder_is_cloned_into()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var cloneDirectory = sandbox.CreateDirectory("empty");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory };

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Assert
        resolved.RepositoryDirectory.Should().Be(cloneDirectory);
        resolved.Identity.Commit.Should().Be(upstream.Commit2);
        resolved.Identity.CloneReused.Should().BeFalse("an empty folder is cloned into, not reused");
    }

    [Fact]
    public async Task git_clean_up_after_build_deletes_the_clone()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var cloneDirectory = Path.Combine(sandbox.Directory, "my-clone");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory, CleanUpAfterBuild = true };
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act
        resolved.CleanUp();

        //Assert
        Directory.Exists(cloneDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task git_missing_branch_fails_and_removes_the_fresh_clone()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var cloneDirectory = Path.Combine(sandbox.Directory, "my-clone");
        var source = new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { CloneDirectory = cloneDirectory, Branch = "no-such-branch" };
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.Message.Should().Contain("'no-such-branch' does not exist").And.Contain("feature");
        Directory.Exists(cloneDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task git_missing_commit_and_missing_tag_fail()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        Func<Task> commit = () => new SourceResolver().ResolveAsync(new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { Commit = new string('0', 40) }, StagingContexts.Create(run), TestContext.Current.CancellationToken);
        Func<Task> tag = () => new SourceResolver().ResolveAsync(new GitRepositorySource(upstream.Url, GitTestRepository.ManifestPath) { Tag = "v9.9.9" }, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act + Assert
        await commit.Should().ThrowAsync<SourceResolutionException>();
        (await tag.Should().ThrowAsync<SourceResolutionException>()).Which.Message.Should().Contain("'v9.9.9' does not exist");
    }

    [Fact]
    public async Task git_manifest_missing_from_the_checkout_fails()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (upstream, run) = Upstream(sandbox);
        var source = new GitRepositorySource(upstream.Url, "elsewhere/Cargo.toml");
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(run), TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.Message.Should().Contain("has no 'elsewhere/Cargo.toml' at commit " + upstream.Commit2);
        exception.InnerException.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task git_more_than_one_reference_is_an_argument_exception()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var source = new GitRepositorySource("https://example.invalid/repo.git") { Branch = "main", Tag = "v1" };
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(Path.Combine(sandbox.Directory, "run")), TestContext.Current.CancellationToken);

        //Act + Assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("at most one");
    }

    [Fact]
    public async Task git_unreachable_remote_is_a_source_resolution_exception_with_the_libgit2_cause()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var missing = new Uri(Path.Combine(sandbox.Directory, "no-such-repository")).AbsoluteUri;
        var source = new GitRepositorySource(missing);
        Func<Task> act = () => new SourceResolver().ResolveAsync(source, StagingContexts.Create(Path.Combine(sandbox.Directory, "run")), TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<SourceResolutionException>()).Which;

        //Assert
        exception.InnerException.Should().BeAssignableTo<LibGit2SharpException>();
        Directory.Exists(Path.Combine(sandbox.Directory, "run", "clone", "no-such-repository")).Should().BeFalse();
    }

    // ---- live: a real public GitHub repository ----

    [Fact]
    public async Task live_clone_of_the_public_github_repository_at_its_head()
    {
        //Arrange
        Capabilities.SkipOnWindowsPathLimit("libgit2 cannot check out this repository's deepest files (path too long)");
        if (!await Capabilities.CanReachGitHubAsync())
        {
            Assert.Skip("No network route to github.com:443 (3 shared probes of 20 s timed out or were refused); the live GitHub clone test needs the internet.");
        }

        using var sandbox = SampleSandbox.CreateEmpty();
        var source = new GitRepositorySource("https://github.com/ellisnet/CodeBrix.RustTools", "rust-samples/test_project_02/Cargo.toml");

        //Act
        var resolved = await new SourceResolver().ResolveAsync(source, StagingContexts.Create(Path.Combine(sandbox.Directory, "run")), TestContext.Current.CancellationToken);

        //Assert
        resolved.RepositoryDirectory.Should().Be(Path.Combine(sandbox.Directory, "run", "clone", "CodeBrix.RustTools"));
        resolved.Identity.Commit.Should().MatchRegex("^[0-9a-f]{40}$");
        resolved.Identity.CrateName.Should().NotBeNullOrEmpty();
        File.Exists(resolved.ManifestPath).Should().BeTrue();
    }
}
