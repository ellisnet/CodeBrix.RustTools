using System;
using System.Linq;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Engine;

public class DockerGuidanceProviderTests
{
    [Theory]
    [InlineData(OperatingSystemKind.Windows)]
    [InlineData(OperatingSystemKind.MacOS)]
    [InlineData(OperatingSystemKind.DebianLinux)]
    [InlineData(OperatingSystemKind.OtherLinux)]
    [InlineData(OperatingSystemKind.Unknown)]
    public void GetGuidance_yields_commands_for_every_topic(OperatingSystemKind os)
    {
        foreach (var topic in Enum.GetValues<DockerGuidanceTopic>())
        {
            //Act
            var guidance = DockerGuidanceProvider.GetGuidance(topic, os);

            //Assert
            guidance.Topic.Should().Be(topic);
            guidance.OperatingSystem.Should().Be(os);
            guidance.Title.Should().NotBeNullOrWhiteSpace();
            guidance.Explanation.Should().NotBeNullOrWhiteSpace();
            guidance.Commands.Should().NotBeEmpty();
            guidance.DocumentationUrl.Should().StartWith("https://");
        }
    }

    [Fact]
    public void GetGuidance_installs_docker_desktop_with_winget_on_windows()
        => DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.InstallDocker, OperatingSystemKind.Windows)
            .Commands.Should().Contain("winget install --exact --id Docker.DockerDesktop");

    [Fact]
    public void GetGuidance_installs_docker_ce_from_the_debian_repository()
    {
        //Act
        var guidance = DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.InstallDocker, OperatingSystemKind.DebianLinux);

        //Assert
        guidance.Commands.Should().Contain(c => c.Contains("https://download.docker.com/linux/debian"));
        guidance.Commands.Last().Should().Be("sudo apt-get install docker-ce docker-ce-cli containerd.io docker-buildx-plugin");
        guidance.RequiresAdministrator.Should().BeTrue();
    }

    [Fact]
    public void GetGuidance_adds_the_user_to_the_docker_group_on_linux()
    {
        //Act
        var guidance = DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.GrantDockerAccess, OperatingSystemKind.DebianLinux);

        //Assert
        guidance.Commands.Should().Contain("sudo usermod -aG docker $USER");
        guidance.RequiresNewSession.Should().BeTrue();
    }

    [Fact]
    public void GetGuidance_starts_docker_with_systemctl_on_linux()
        => DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.StartDocker, OperatingSystemKind.OtherLinux)
            .Commands.Should().Contain("sudo systemctl start docker");

    [Fact]
    public void GetGuidance_opens_docker_desktop_on_macos()
        => DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.StartDocker, OperatingSystemKind.MacOS)
            .Commands.Should().ContainSingle().Which.Should().Be("open -a Docker");

    [Fact]
    public void GetGuidance_mentions_wsl_on_windows()
        => DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.EnableWsl2Backend, OperatingSystemKind.Windows)
            .Commands.Should().Contain("wsl --update");

    [Fact]
    public void ForIssues_is_empty_without_issues()
        => DockerGuidanceProvider.ForIssues(Array.Empty<DockerEngineIssue>(), OperatingSystemKind.DebianLinux).Should().BeEmpty();

    [Fact]
    public void ForIssues_treats_null_as_no_issues()
        => DockerGuidanceProvider.ForIssues(null, OperatingSystemKind.Windows).Should().BeEmpty();

    [Fact]
    public void ForIssues_installs_then_starts_then_grants_access_on_linux()
        => DockerGuidanceProvider.ForIssues(new[] { DockerEngineIssue.NotReachable }, OperatingSystemKind.DebianLinux)
            .Select(g => g.Topic).Should().Equal(
                DockerGuidanceTopic.InstallDocker, DockerGuidanceTopic.StartDocker, DockerGuidanceTopic.GrantDockerAccess);

    [Fact]
    public void ForIssues_includes_wsl_on_windows()
        => DockerGuidanceProvider.ForIssues(new[] { DockerEngineIssue.NotReachable }, OperatingSystemKind.Windows)
            .Select(g => g.Topic).Should().Equal(
                DockerGuidanceTopic.InstallDocker, DockerGuidanceTopic.EnableWsl2Backend, DockerGuidanceTopic.StartDocker);

    [Fact]
    public void ForIssues_does_not_repeat_a_topic()
        => DockerGuidanceProvider.ForIssues(new[] { DockerEngineIssue.NotReachable, DockerEngineIssue.PermissionDenied }, OperatingSystemKind.DebianLinux)
            .Select(g => g.Topic).Should().OnlyHaveUniqueItems();

    [Theory]
    [InlineData(DockerEngineIssue.PermissionDenied, DockerGuidanceTopic.GrantDockerAccess)]
    [InlineData(DockerEngineIssue.NotLinuxDaemon, DockerGuidanceTopic.SwitchToLinuxContainers)]
    [InlineData(DockerEngineIssue.EndpointNotSupported, DockerGuidanceTopic.CheckEndpoint)]
    [InlineData(DockerEngineIssue.DockerCliNotFound, DockerGuidanceTopic.InstallDockerCli)]
    public void ForIssues_maps_each_issue_to_its_topic(DockerEngineIssue issue, DockerGuidanceTopic topic)
        => DockerGuidanceProvider.ForIssues(new[] { issue }, OperatingSystemKind.Windows)
            .Should().ContainSingle().Which.Topic.Should().Be(topic);

    [Fact]
    public void ForEngine_uses_the_host_operating_system_of_the_check()
        => DockerGuidanceProvider.ForEngine(new DockerEngineInfo { Issues = new[] { DockerEngineIssue.NotReachable }, HostOperatingSystem = OperatingSystemKind.MacOS })
            .Should().OnlyContain(g => g.OperatingSystem == OperatingSystemKind.MacOS);

    [Fact]
    public void ForEngine_throws_for_null()
    {
        //Act
        var act = () => DockerGuidanceProvider.ForEngine(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DockerGuidance_ToString_lists_title_commands_and_link()
    {
        //Act
        var text = DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.StartDocker, OperatingSystemKind.DebianLinux).ToString();

        //Assert
        text.Should().StartWith("Start the Docker service");
        text.Should().Contain("    sudo systemctl start docker");
        text.Should().Contain("More information: https://");
    }
}
