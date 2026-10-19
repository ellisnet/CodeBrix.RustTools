using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

public class WorkspaceStagerTests
{
    private const string GammaLine = "gamma = { path = \"../../external/gamma\" } # keep this comment";

    private static async Task<StagedWorkspace> StageAsync(string manifest, string runDirectory, List<CrateBuildProgress> reports = null, CrateBuilderContext context = null)
    {
        context ??= StagingContexts.Create(runDirectory, reports);
        var resolved = await new SourceResolver().ResolveAsync(new LocalManifestSource(manifest), context, TestContext.Current.CancellationToken);
        return await new WorkspaceStager().StageAsync(resolved, new CrateBuildRequest(), context, TestContext.Current.CancellationToken);
    }

    private static string Alpha(SampleSandbox sandbox) => Path.Combine(sandbox.Directory, "crates", "alpha", "Cargo.toml");

    private static string RunDirectory(SampleSandbox sandbox, string name = "run") => Path.Combine(Path.GetDirectoryName(sandbox.Directory), name);

    /// <summary>Moves external/gamma out of the workspace folder (to a sibling "outside"
    /// folder) and points alpha's path dependency at it: a dependency outside the copied
    /// tree.</summary>
    private static string MoveGammaOutside(SampleSandbox sandbox)
    {
        var outside = Path.Combine(Path.GetDirectoryName(sandbox.Directory), "outside");
        Directory.CreateDirectory(outside);
        Directory.Move(Path.Combine(sandbox.Directory, "external", "gamma"), Path.Combine(outside, "gamma"));
        var manifest = Alpha(sandbox);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("../../external/gamma", "../../../outside/gamma"));
        return Path.Combine(outside, "gamma");
    }

    [Fact]
    public async Task stages_the_whole_workspace_with_the_lock_file_and_fills_the_identity()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var run = RunDirectory(sandbox);
        var upstreamAlpha = File.ReadAllText(Alpha(sandbox));

        //Act
        var staged = await StageAsync(Alpha(sandbox), run);

        //Assert
        staged.Directory.Should().Be(Path.Combine(run, "stage", "alpha"));
        staged.WorkspaceManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "Cargo.toml"));
        staged.ManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "crates", "alpha", "Cargo.toml"));
        staged.UpstreamManifestPath.Should().Be(staged.ManifestPath);
        staged.IsFacade.Should().BeFalse();
        staged.CrateName.Should().Be("alpha");
        staged.PathRewrites.Should().BeEmpty("gamma lives inside the copied workspace folder");
        staged.GitDependencies.Should().BeEmpty();
        File.Exists(Path.Combine(staged.Directory, "src", "Cargo.lock")).Should().BeTrue();
        File.Exists(Path.Combine(staged.Directory, "src", "external", "gamma", "src", "lib.rs")).Should().BeTrue();
        File.Exists(Path.Combine(staged.Directory, "src", "crates", "beta", "src", "lib.rs")).Should().BeTrue();
        Directory.Exists(Path.Combine(staged.Directory, WorkspaceStager.VendorFolderName)).Should().BeFalse();
        staged.Identity.Kind.Should().Be(CrateSourceIdentity.LocalKind);
        staged.Identity.CrateName.Should().Be("alpha");
        staged.Identity.CrateVersion.Should().Be("0.18.2");
        staged.Identity.CrateTypes.Should().Equal("rlib");
        staged.Identity.SourceHash.Should().MatchRegex("^[0-9a-f]{64}$");
        staged.Identity.LocalManifestPath.Should().Be(Alpha(sandbox));
        File.ReadAllText(Alpha(sandbox)).Should().Be(upstreamAlpha);
    }

    [Fact]
    public async Task a_scratch_root_inside_the_crate_and_codebrix_folders_at_any_depth_are_not_staged()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var scratch = Path.Combine(sandbox.Directory, "crates", "alpha", "scratch");
        var run = Path.Combine(scratch, "runs", "run-1");
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(scratch, "leftover.txt"), "from an earlier run\n");
        var oldFacade = Path.Combine(sandbox.Directory, "crates", "alpha", ".codebrix-facade", "alpha_facade");
        Directory.CreateDirectory(oldFacade);
        File.WriteAllText(Path.Combine(oldFacade, "lib.rs"), "// generated earlier\n");
        var context = new CrateBuilderContext(new CrateBuilderOptions { ScratchRootDirectory = scratch }, run, null, null, null);

        //Act
        var staged = await StageAsync(Alpha(sandbox), run, context: context);

        //Assert
        staged.Directory.Should().Be(Path.Combine(run, "stage", "alpha"));
        var stagedAlpha = Path.Combine(staged.Directory, "src", "crates", "alpha");
        File.Exists(Path.Combine(stagedAlpha, "Cargo.toml")).Should().BeTrue();
        Directory.Exists(Path.Combine(stagedAlpha, "scratch")).Should().BeFalse();
        Directory.Exists(Path.Combine(stagedAlpha, ".codebrix-facade")).Should().BeFalse();
        Directory.GetDirectories(staged.Directory, "stage", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task leaves_out_git_cargo_target_and_tool_folders_but_stages_other_hidden_root_folders()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        sandbox.WriteFile(Path.Combine("target", "CACHEDIR.TAG"), "Signature: 8a477f597d28d172789f06886806bc55\n");
        sandbox.WriteFile(Path.Combine("target", "debug", "libalpha.rlib"), "binary");
        sandbox.WriteFile(Path.Combine("crates", "beta", "target", "debug", "out.txt"), "old standalone build");
        sandbox.WriteFile(Path.Combine(".git", "HEAD"), "ref: refs/heads/main\n");
        sandbox.WriteFile(Path.Combine(".github", "workflows", "ci.yml"), "name: ci\n");
        sandbox.WriteFile(Path.Combine("crates", "alpha", ".git"), "gitdir: ../../.git/modules/alpha\n");
        sandbox.WriteFile(Path.Combine("crates", "alpha", "src", "target", "mod.rs"), "// a source module folder named target\n");
        sandbox.WriteFile(Path.Combine("crates", "alpha", ".cargo", "config.toml"), "[build]\n");
        sandbox.WriteFile(Path.Combine("crates", "beta", "nested", ".git", "config"), "[core]\n");
        sandbox.WriteFile(Path.Combine(".vscode", "settings.json"), "{}\n");
        sandbox.WriteFile(Path.Combine(".idea", "workspace.xml"), "<project />\n");
        sandbox.WriteFile(Path.Combine(".cargo", "config.toml"), "[env]\nSHAPE = \"1\"\n");
        sandbox.WriteFile(Path.Combine(".sqlx", "query-0001.json"), "{}\n");

        //Act
        var staged = await StageAsync(Alpha(sandbox), RunDirectory(sandbox));

        //Assert
        var src = Path.Combine(staged.Directory, "src");
        Directory.Exists(Path.Combine(src, "target")).Should().BeFalse();
        Directory.Exists(Path.Combine(src, "crates", "beta", "target")).Should().BeFalse();
        Directory.Exists(Path.Combine(src, ".git")).Should().BeFalse();
        Directory.Exists(Path.Combine(src, ".github")).Should().BeFalse();
        File.Exists(Path.Combine(src, "crates", "alpha", ".git")).Should().BeFalse();
        Directory.Exists(Path.Combine(src, "crates", "beta", "nested", ".git")).Should().BeFalse();
        File.Exists(Path.Combine(src, "crates", "alpha", "src", "target", "mod.rs")).Should().BeTrue();
        File.Exists(Path.Combine(src, "crates", "alpha", ".cargo", "config.toml")).Should().BeTrue();
        Directory.Exists(Path.Combine(src, ".vscode")).Should().BeFalse();
        Directory.Exists(Path.Combine(src, ".idea")).Should().BeFalse();
        File.Exists(Path.Combine(src, ".cargo", "config.toml")).Should().BeTrue("cargo reads the workspace's configuration");
        File.Exists(Path.Combine(src, ".sqlx", "query-0001.json")).Should().BeTrue("a build may read a hidden root folder (offline query data)");
    }

    [Fact]
    public async Task a_dependency_in_an_enclosing_workspace_is_vendored_with_that_workspace_root()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("outer/Cargo.toml", "[workspace]\nmembers = [\"crates/core\"]\nexclude = [\"crates/core/contract\"]\nresolver = \"2\"\n\n[workspace.package]\nedition = \"2021\"\nversion = \"0.3.0\"\n");
        sandbox.WriteFile("outer/crates/core/Cargo.toml", "[package]\nname = \"shape_core\"\nversion.workspace = true\nedition.workspace = true\n");
        sandbox.WriteFile("outer/crates/core/src/lib.rs", "pub fn base() -> i32 { 7 }\n");
        var contract = sandbox.WriteFile("outer/crates/core/contract/Cargo.toml", "[package]\nname = \"shape_contract\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[workspace]\n\n[dependencies]\nshape_core = { path = \"..\" }\n");
        sandbox.WriteFile("outer/crates/core/contract/src/lib.rs", "pub fn doubled() -> i32 { shape_core::base() * 2 }\n");

        //Act
        var staged = await StageAsync(contract, RunDirectory(sandbox));

        //Assert
        staged.WorkspaceManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "Cargo.toml"));
        var vendored = Path.Combine(staged.Directory, ".codebrix-vendor", "1-outer");
        File.ReadAllText(Path.Combine(vendored, "Cargo.toml")).Should().Contain("[workspace.package]", "the dependency inherits from its workspace root, so the root is vendored");
        var rewrite = staged.PathRewrites.Should().ContainSingle().Which;
        rewrite.NewPath.Should().Be("../.codebrix-vendor/1-outer/crates/core");
    }

    [Fact]
    public async Task a_path_dependency_outside_the_workspace_is_vendored_and_the_staged_path_rewritten()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var gamma = MoveGammaOutside(sandbox);
        var upstreamAlpha = File.ReadAllText(Alpha(sandbox));
        var reports = new List<CrateBuildProgress>();

        //Act
        var staged = await StageAsync(Alpha(sandbox), RunDirectory(sandbox), reports);

        //Assert
        var vendored = Path.Combine(staged.Directory, ".codebrix-vendor", "1-gamma");
        File.Exists(Path.Combine(vendored, "Cargo.toml")).Should().BeTrue();
        File.Exists(Path.Combine(vendored, "src", "lib.rs")).Should().BeTrue();
        var rewrite = staged.PathRewrites.Should().ContainSingle().Which;
        rewrite.ManifestPath.Should().Be(staged.ManifestPath);
        rewrite.DependencyName.Should().Be("gamma");
        rewrite.OldPath.Should().Be("../../../outside/gamma");
        rewrite.NewPath.Should().Be("../../../.codebrix-vendor/1-gamma");
        var stagedText = File.ReadAllText(staged.ManifestPath);
        stagedText.Should().Contain("gamma = { path = \"../../../.codebrix-vendor/1-gamma\" } # keep this comment");
        stagedText.Should().Be(upstreamAlpha.Replace("../../../outside/gamma", "../../../.codebrix-vendor/1-gamma"), "only the path token changes");
        File.ReadAllText(Alpha(sandbox)).Should().Be(upstreamAlpha);
        Directory.Exists(gamma).Should().BeTrue();
        reports.Should().Contain(r => r.Stage == CrateBuildStage.Staging && r.Message.StartsWith("Rewrote "));
    }

    [Fact]
    public async Task vendored_crates_path_dependencies_are_followed_in_turn()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        MoveGammaOutside(sandbox);
        var outside = Path.Combine(Path.GetDirectoryName(sandbox.Directory), "outside");
        Directory.CreateDirectory(Path.Combine(outside, "delta"));
        File.WriteAllText(Path.Combine(outside, "delta", "Cargo.toml"), "[package]\nname = \"delta\"\nversion = \"0.1.0\"\nedition = \"2024\"\n");
        var gammaManifest = Path.Combine(outside, "gamma", "Cargo.toml");
        File.WriteAllText(gammaManifest, File.ReadAllText(gammaManifest) + "\n[dependencies.delta]\npath = '../delta'\n");

        //Act
        var staged = await StageAsync(Alpha(sandbox), RunDirectory(sandbox));

        //Assert
        staged.PathRewrites.Select(r => r.DependencyName).Should().Equal("gamma", "delta");
        var deltaRewrite = staged.PathRewrites[1];
        deltaRewrite.ManifestPath.Should().Be(Path.Combine(staged.Directory, ".codebrix-vendor", "1-gamma", "Cargo.toml"));
        deltaRewrite.NewPath.Should().Be("../2-delta");
        File.ReadAllText(deltaRewrite.ManifestPath).Should().Contain("path = '../2-delta'", "a literal string stays a literal string");
        File.Exists(Path.Combine(staged.Directory, ".codebrix-vendor", "2-delta", "Cargo.toml")).Should().BeTrue();
    }

    [Fact]
    public async Task source_hash_is_stable_across_stagings_and_changes_with_one_byte()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);

        //Act
        var first = await StageAsync(Alpha(sandbox), RunDirectory(sandbox, "run-1"));
        var second = await StageAsync(Alpha(sandbox), RunDirectory(sandbox, "run-2"));
        var lib = Path.Combine(sandbox.Directory, "crates", "beta", "src", "lib.rs");
        File.WriteAllText(lib, File.ReadAllText(lib).Replace("n.wrapping_add(1)", "n.wrapping_add(2)"));
        var edited = await StageAsync(Alpha(sandbox), RunDirectory(sandbox, "run-3"));

        //Assert
        second.Identity.SourceHash.Should().Be(first.Identity.SourceHash);
        edited.Identity.SourceHash.Should().NotBe(first.Identity.SourceHash);
        SourceHasher.Compute(first.Directory, TestContext.Current.CancellationToken).Should().Be(first.Identity.SourceHash);
    }

    [Fact]
    public async Task git_dependencies_are_listed_and_not_staged()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var beta = Path.Combine(sandbox.Directory, "crates", "beta", "Cargo.toml");
        File.WriteAllText(beta, File.ReadAllText(beta)
            + "\n[target.'cfg(any())'.dependencies]\nremote = { git = \"https://example.invalid/remote.git\", rev = \"abc123\" }\n"
            + "\n[build-dependencies]\nhelper = { git = \"https://example.invalid/helper.git\" }\n");

        //Act
        var staged = await StageAsync(Alpha(sandbox), RunDirectory(sandbox));

        //Assert
        staged.GitDependencies.Should().Equal("helper @ https://example.invalid/helper.git", "remote @ https://example.invalid/remote.git#abc123");
    }

    [Fact]
    public async Task a_dangling_path_dependency_is_a_warning_not_a_failure()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var beta = Path.Combine(sandbox.Directory, "crates", "beta", "Cargo.toml");
        File.WriteAllText(beta, File.ReadAllText(beta) + "\n[dev-dependencies]\nghost = { path = \"../../../nowhere/ghost\" }\n");
        var context = StagingContexts.Create(RunDirectory(sandbox));

        //Act
        var staged = await StageAsync(Alpha(sandbox), null, null, context);

        //Assert
        staged.PathRewrites.Should().BeEmpty();
        context.Warnings.Should().ContainSingle().Which.Should().Contain("'ghost'").And.Contain("holds no Cargo.toml");
    }

    [Fact]
    public async Task a_standalone_crate_is_its_own_workspace_root()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var gamma = Path.Combine(sandbox.Directory, "external", "gamma", "Cargo.toml");

        //Act
        var staged = await StageAsync(gamma, RunDirectory(sandbox));

        //Assert
        staged.Directory.Should().EndWith(Path.Combine("stage", "gamma"));
        staged.WorkspaceManifestPath.Should().Be(staged.ManifestPath);
        staged.ManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "Cargo.toml"));
        File.Exists(Path.Combine(staged.Directory, "src", "src", "lib.rs")).Should().BeTrue();
        staged.Identity.CrateVersion.Should().Be("1.2.0");
    }

    [Fact]
    public async Task a_crate_inside_a_workspace_that_does_not_list_it_is_a_staging_exception()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var stray = sandbox.WriteFile(Path.Combine("stray", "Cargo.toml"), "[package]\nname = \"stray\"\nversion = \"0.1.0\"\nedition = \"2024\"\n");
        var context = StagingContexts.Create(RunDirectory(sandbox));
        var resolved = new ResolvedSource { ManifestPath = stray, RepositoryDirectory = Path.GetDirectoryName(stray), Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind } };
        Func<Task> act = () => new WorkspaceStager().StageAsync(resolved, new CrateBuildRequest(), context, TestContext.Current.CancellationToken);

        //Act
        var exception = (await act.Should().ThrowAsync<StagingException>()).Which;

        //Assert
        exception.Message.Should().Contain("is not one of its members");
        exception.Stage.Should().Be(CrateBuildStage.Staging);
    }

    [Fact]
    public async Task an_unlisted_path_dependency_of_a_member_is_an_implicit_member()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var root = Path.Combine(sandbox.Directory, "Cargo.toml");
        File.WriteAllText(root, File.ReadAllText(root).Replace("exclude = [\"external\"]", "exclude = []"));
        var gamma = Path.Combine(sandbox.Directory, "external", "gamma", "Cargo.toml");

        //Act
        var staged = await StageAsync(gamma, RunDirectory(sandbox));

        //Assert
        staged.WorkspaceManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "Cargo.toml"));
        staged.ManifestPath.Should().Be(Path.Combine(staged.Directory, "src", "external", "gamma", "Cargo.toml"));
    }

    [Fact]
    public async Task live_the_staged_workspace_with_a_vendored_dependency_builds_with_rust_workbench()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(18);
        MoveGammaOutside(sandbox);
        var staged = await StageAsync(Alpha(sandbox), RunDirectory(sandbox));
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(staged.WorkspaceManifestPath);
        var options = new CargoBuildOptions { Offline = true, Locked = true, Packages = { "alpha" } };

        //Act
        var result = await workbench.TestAsync(project, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.TestsPassed.Should().Be(1);
        result.Artifacts.Should().Contain(a => a.PackageName == "gamma" && a.ManifestPath.Contains(".codebrix-vendor"));
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse("the upstream tree is never built in");
    }
}
