using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Internal;

public class BuildCachePrunerTests
{
    private const string BuildxOutput =
        "ID                                              RECLAIMABLE     SIZE            LAST ACCESSED\n" +
        "k3l9hq2c0b6m3qjv8w5x1y7z4                       true            1.2GB           2 hours ago\n" +
        "pzr4m1n7q2w8e3r6t9y0u5i1o                       true            34.5MB          2 hours ago\n" +
        "Total:\t1.234GB\n";

    [Fact]
    public void PruneArguments_are_builder_prune_all_force()
        => BuildCachePruner.PruneArguments.Should().Equal("builder", "prune", "--all", "--force");

    [Fact]
    public async Task PruneAsync_runs_the_prune_and_reads_the_reclaimed_space()
    {
        //Arrange
        IReadOnlyList<string> seen = null;
        Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
        {
            seen = args;
            return Task.FromResult(new DockerCliOutput(0, BuildxOutput, string.Empty));
        }

        //Act
        var outcome = await BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        seen.Should().Equal("builder", "prune", "--all", "--force");
        outcome.Pruned.Should().BeTrue();
        outcome.ReclaimedSpace.Should().Be("1.234GB");
        outcome.ReclaimedBytes.Should().Be(1_234_000_000L);
        outcome.Output.Should().Be(BuildxOutput);
        outcome.Failure.Should().BeNull();
    }

    [Fact]
    public async Task PruneAsync_with_an_empty_cache_reports_zero_bytes()
    {
        //Arrange
        static Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
            => Task.FromResult(new DockerCliOutput(0, "Total:\t0B\n", string.Empty));

        //Act
        var outcome = await BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        outcome.Pruned.Should().BeTrue();
        outcome.ReclaimedSpace.Should().Be("0B");
        outcome.ReclaimedBytes.Should().Be(0L);
    }

    [Fact]
    public async Task PruneAsync_succeeds_without_a_total_line()
    {
        //Arrange
        static Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
            => Task.FromResult(new DockerCliOutput(0, string.Empty, string.Empty));

        //Act
        var outcome = await BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        outcome.Pruned.Should().BeTrue();
        outcome.ReclaimedSpace.Should().BeNull();
        outcome.ReclaimedBytes.Should().BeNull();
        outcome.Failure.Should().BeNull();
    }

    [Fact]
    public async Task PruneAsync_reports_a_non_zero_exit_without_throwing()
    {
        //Arrange
        static Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
            => Task.FromResult(new DockerCliOutput(1, string.Empty, "ERROR: permission denied\n"));

        //Act
        var outcome = await BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        outcome.Pruned.Should().BeFalse();
        outcome.Failure.Should().Contain("exited with code 1").And.Contain("permission denied");
        outcome.Output.Should().Contain("permission denied");
    }

    [Fact]
    public async Task PruneAsync_reports_a_cli_that_cannot_start_without_throwing()
    {
        //Arrange
        static Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
            => throw new DockerCliException("docker builder prune --all --force", -1, "The docker command-line tool 'docker' was not found.");

        //Act
        var outcome = await BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        outcome.Pruned.Should().BeFalse();
        outcome.Output.Should().BeNull();
        outcome.Failure.Should().StartWith("Build cache:").And.Contain("was not found");
    }

    [Fact]
    public async Task PruneAsync_lets_cancellation_through()
    {
        //Arrange
        static Task<DockerCliOutput> Run(IReadOnlyList<string> args, CancellationToken token)
            => throw new OperationCanceledException();

        //Act
        var act = () => BuildCachePruner.PruneAsync(Run, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunCliAsync_reports_a_missing_cli_as_a_DockerCliException()
    {
        //Arrange
        var missing = Path.Combine(Path.GetTempPath(), "no-such-docker-" + Guid.NewGuid().ToString("N"));
        var options = new DockerClientOptions { DockerCliPath = missing };

        //Act
        var act = () => BuildCachePruner.RunCliAsync(options, BuildCachePruner.PruneArguments, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<DockerCliException>()).Which.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task PruneAsync_through_a_missing_cli_is_reported_not_thrown()
    {
        //Arrange
        var options = new DockerClientOptions { DockerCliPath = Path.Combine(Path.GetTempPath(), "no-such-docker-" + Guid.NewGuid().ToString("N")) };

        //Act
        var outcome = await BuildCachePruner.PruneAsync(
            (args, token) => BuildCachePruner.RunCliAsync(options, args, token),
            TestContext.Current.CancellationToken);

        //Assert
        outcome.Pruned.Should().BeFalse();
        outcome.Failure.Should().Contain("was not found");
    }

    [Theory]
    [InlineData("Total:\t1.234GB", "1.234GB")]
    [InlineData("Total:   512kB", "512kB")]
    [InlineData("Total reclaimed space: 2.5MB", "2.5MB")]
    [InlineData("total: 0B", "0B")]
    [InlineData("Total:\t3GB\r\n", "3GB")]
    [InlineData("no total here", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseReclaimedSpace_reads_the_total_line(string output, string expected)
        => BuildCachePruner.ParseReclaimedSpace(output).Should().Be(expected);

    [Fact]
    public void ParseReclaimedSpace_takes_the_last_total_line()
        => BuildCachePruner.ParseReclaimedSpace("Total: 1MB\nmore\nTotal: 2MB\n").Should().Be("2MB");

    [Theory]
    [InlineData("0B", 0L)]
    [InlineData("17B", 17L)]
    [InlineData("1.5kB", 1_500L)]
    [InlineData("1.5KB", 1_500L)]
    [InlineData("34.5MB", 34_500_000L)]
    [InlineData("1.234GB", 1_234_000_000L)]
    [InlineData("2TB", 2_000_000_000_000L)]
    [InlineData("1KiB", 1_024L)]
    [InlineData("1.5MiB", 1_572_864L)]
    [InlineData("1GiB", 1_073_741_824L)]
    public void ParseSize_reads_decimal_and_binary_units(string text, long expected)
        => BuildCachePruner.ParseSize(text).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("1.2XB")]
    [InlineData("-1GB")]
    public void ParseSize_returns_null_for_unreadable_text(string text)
        => BuildCachePruner.ParseSize(text).Should().BeNull();
}
