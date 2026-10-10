using CodeBrix.RustTools.Docker;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests;

public class CleanupOptionsTests
{
    [Fact]
    public void defaults_remove_containers_only_from_every_workbench()
    {
        //Act
        var options = new CleanupOptions();

        //Assert
        options.RemoveContainers.Should().BeTrue();
        options.RemoveImages.Should().BeFalse();
        options.RemoveVolumes.Should().BeFalse();
        options.OnlyThisWorkbench.Should().BeFalse();
        options.PruneBuildCache.Should().BeFalse();
    }

    [Fact]
    public void CleanupResult_succeeds_without_failures()
        => new CleanupResult().Succeeded.Should().BeTrue();

    [Fact]
    public void CleanupResult_fails_with_failures()
        => new CleanupResult { Failures = new[] { "Volume x: in use" } }.Succeeded.Should().BeFalse();

    [Fact]
    public void CleanupResult_reports_no_build_cache_prune_by_default()
    {
        //Act
        var result = new CleanupResult();

        //Assert
        result.BuildCachePruned.Should().BeFalse();
        result.BuildCacheReclaimedSpace.Should().BeNull();
        result.BuildCacheReclaimedBytes.Should().BeNull();
        result.BuildCachePruneOutput.Should().BeNull();
    }
}
