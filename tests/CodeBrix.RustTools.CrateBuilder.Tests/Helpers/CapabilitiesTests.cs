using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

public class CapabilitiesTests
{
    [Fact]
    public void the_github_probe_is_one_shared_probe_with_retries_and_a_long_timeout()
    {
        //Act
        var first = Capabilities.CanReachGitHubAsync();
        var second = Capabilities.CanReachGitHubAsync();

        //Assert
        second.Should().BeSameAs(first);
        Capabilities.GitHubProbeAttempts.Should().BeGreaterThanOrEqualTo(2);
        Capabilities.GitHubProbeTimeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(15));
    }
}
