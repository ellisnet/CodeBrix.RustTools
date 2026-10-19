using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// The novice path from a URL: this repository's public GitHub copy is cloned at its HEAD,
/// rust-samples/test_project_13 inside it is built in host mode and wrapped; the same request
/// again reuses the clone, and with CleanUpAfterBuild the clone folder is deleted. Skips without
/// a route to github.com:443, without cargo or without the dotnet CLI.
/// </summary>
public class GitEndToEndTests
{
    private const string RepositoryUrl = "https://github.com/ellisnet/CodeBrix.RustTools";
    private const string ManifestPath = "rust-samples/test_project_13/Cargo.toml";

    [Fact]
    public async Task a_crate_in_a_public_github_repository_is_cloned_built_wrapped_then_the_clone_is_reused_and_cleaned_up()
    {
        //Arrange
        if (!await Capabilities.CanReachGitHubAsync())
        {
            Assert.Skip("No network route to github.com:443 (3 shared probes of 20 s timed out or were refused); the end-to-end Git test needs the internet.");
        }

        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        var cloneDirectory = Path.Combine(sandbox.Directory, "clone");
        var request = new CrateBuildRequest
        {
            Source = new GitRepositorySource(RepositoryUrl, ManifestPath) { CloneDirectory = cloneDirectory },
            Mode = BuildMode.Host,
            Targets = { target },
            OutputDirectory = Path.Combine(sandbox.Directory, "out", "native"),
            Wrapper = new WrapperOptions { AssemblyName = "E2E.FromGit", RootNamespace = "E2E.FromGit" },
        };
        using var builder = EndToEndKit.NewBuilder();
        var firstReports = new List<CrateBuildProgress>();
        var secondReports = new List<CrateBuildProgress>();

        //Act
        var first = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(firstReports.Add), TestContext.Current.CancellationToken);
        var cloneAfterFirst = File.Exists(Path.Combine(cloneDirectory, ManifestPath));
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(sandbox.Directory, "app"), first.Wrapper?.ProjectPath ?? "missing.csproj",
            "using System;\nusing E2E.FromGit;\n\nConsole.WriteLine(\"add(20,22)=\" + TestProject13.CbAdd(20, 22));\n");
        ((GitRepositorySource)request.Source).CleanUpAfterBuild = true;
        var second = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(secondReports.Add), TestContext.Current.CancellationToken);

        //Assert
        first.Success.Should().BeTrue(first.ToString());
        first.Source.Kind.Should().Be(CrateSourceIdentity.GitKind);
        first.Source.Url.Should().Be(RepositoryUrl);
        first.Source.Commit.Should().MatchRegex("^[0-9a-f]{40}$");
        first.Source.ManifestPath.Should().Be(ManifestPath);
        first.Record.Source.Commit.Should().Be(first.Source.Commit);
        first.Source.CloneReused.Should().BeFalse("the first build cloned into the empty folder");
        first.Record.Source.CloneReused.Should().BeFalse();
        cloneAfterFirst.Should().BeTrue("the clone stays when CleanUpAfterBuild is false");
        firstReports.Should().Contain(p => p.Stage == CrateBuildStage.ResolvingSource && p.Message.StartsWith("Cloning " + RepositoryUrl, StringComparison.Ordinal));
        EndToEndKit.Lines(output).Should().Equal("add(20,22)=42");

        second.Success.Should().BeTrue(second.ToString());
        secondReports.Should().Contain(p => p.Stage == CrateBuildStage.ResolvingSource && p.Message.StartsWith("Reusing the clone in", StringComparison.Ordinal));
        secondReports.Should().NotContain(p => p.Stage == CrateBuildStage.ResolvingSource && p.Message.StartsWith("Cloning ", StringComparison.Ordinal));
        second.Source.Commit.Should().Be(first.Source.Commit);
        second.Source.CloneReused.Should().BeTrue("the second build fetched into the clone the first one made");
        second.Record.Source.CloneReused.Should().BeTrue();
        File.ReadAllText(second.BuildRecordPath).Should().Contain("\"cloneReused\": true");
        Directory.Exists(cloneDirectory).Should().BeFalse("CleanUpAfterBuild deletes the clone folder");
        File.Exists(Path.Combine(second.Wrapper.ProjectDirectory, "runtimes", CrateTargets.Rid(target), "native", CrateTargets.NativeFileName(target, "test_project_13"))).Should().BeTrue();
        EndToEndKit.Log(nameof(a_crate_in_a_public_github_repository_is_cloned_built_wrapped_then_the_clone_is_reused_and_cleaned_up), stopwatch);
    }
}
