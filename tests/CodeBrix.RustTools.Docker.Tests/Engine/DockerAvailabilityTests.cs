using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Engine;

public class DockerAvailabilityTests
{
    [Fact]
    public async Task CheckAsync_reports_an_unsupported_endpoint_without_throwing()
    {
        //Act
        var info = await DockerAvailability.CheckAsync(new DockerClientOptions { Endpoint = "https://docker.example:2376" }, TestContext.Current.CancellationToken);

        //Assert
        info.IsAvailable.Should().BeFalse();
        info.Issues.Should().Contain(DockerEngineIssue.EndpointNotSupported);
        info.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        info.HostOperatingSystem.Should().Be(HostOperatingSystem.Detect());
    }

    [Fact]
    public async Task CheckAsync_reports_a_missing_socket_as_not_reachable()
    {
        //Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix domain socket endpoints are not used on Windows.");

        //Act
        var info = await DockerAvailability.CheckAsync(
            new DockerClientOptions { Endpoint = "unix:///nonexistent/codebrix-rusttools/docker.sock", DefaultTimeout = TimeSpan.FromSeconds(5) },
            TestContext.Current.CancellationToken);

        //Assert
        info.IsAvailable.Should().BeFalse();
        info.IsLinuxDaemon.Should().BeFalse();
        info.Issues.Should().Contain(DockerEngineIssue.NotReachable);
        info.Endpoint.Should().Be("unix:///nonexistent/codebrix-rusttools/docker.sock");
        info.ServerVersion.Should().BeNull();
    }

    [Fact]
    public void Classify_detects_permission_denied_in_the_message()
        => DockerAvailability.Classify(new DockerException("connect: permission denied")).Should().Be(DockerEngineIssue.PermissionDenied);

    [Fact]
    public void Classify_detects_an_access_denied_socket_error()
        => DockerAvailability.Classify(new DockerException("Cannot connect", new SocketException((int)SocketError.AccessDenied)))
            .Should().Be(DockerEngineIssue.PermissionDenied);

    [Fact]
    public void Classify_detects_unauthorized_access()
        => DockerAvailability.Classify(new DockerException("Cannot connect", new UnauthorizedAccessException()))
            .Should().Be(DockerEngineIssue.PermissionDenied);

    [Fact]
    public void Classify_treats_anything_else_as_not_reachable()
        => DockerAvailability.Classify(new DockerException("Cannot connect to the Docker daemon", new SocketException((int)SocketError.ConnectionRefused)))
            .Should().Be(DockerEngineIssue.NotReachable);

    [Fact]
    public void IsCliOnPath_is_false_for_a_missing_absolute_path()
        => DockerAvailability.IsCliOnPath("/nonexistent/codebrix/docker").Should().BeFalse();

    [Fact]
    public void IsCliOnPath_is_false_for_a_name_that_is_not_on_path()
        => DockerAvailability.IsCliOnPath("codebrix-no-such-docker-cli").Should().BeFalse();

    [Fact]
    public void DockerEngineInfo_maps_architectures_to_linux_triples()
    {
        //Assert
        new DockerEngineInfo { Architecture = "x86_64" }.NativeLinuxTriple.Should().Be("x86_64-unknown-linux-gnu");
        new DockerEngineInfo { Architecture = "aarch64" }.NativeLinuxTriple.Should().Be("aarch64-unknown-linux-gnu");
        new DockerEngineInfo { Architecture = "riscv64" }.NativeLinuxTriple.Should().Be("riscv64gc-unknown-linux-gnu");
        new DockerEngineInfo { Architecture = "s390x" }.NativeLinuxTriple.Should().BeNull();
    }

    [Fact]
    public void DockerEngineInfo_derives_the_native_runtime_identifier_from_the_architecture()
    {
        //Assert
        new DockerEngineInfo { Architecture = "x86_64" }.NativeLinuxRuntimeIdentifier.Should().Be("linux-x64");
        new DockerEngineInfo { Architecture = "amd64" }.NativeLinuxRuntimeIdentifier.Should().Be("linux-x64");
        new DockerEngineInfo { Architecture = "aarch64" }.NativeLinuxRuntimeIdentifier.Should().Be("linux-arm64");
        new DockerEngineInfo { Architecture = "arm64" }.NativeLinuxRuntimeIdentifier.Should().Be("linux-arm64");
        new DockerEngineInfo { Architecture = "riscv64" }.NativeLinuxRuntimeIdentifier.Should().Be("linux-riscv64");
        new DockerEngineInfo { Architecture = "s390x" }.NativeLinuxRuntimeIdentifier.Should().BeNull();
    }

    [Fact]
    public void DockerEngineInfo_ToString_names_the_issues_when_unavailable()
        => new DockerEngineInfo { Endpoint = "unix:///x.sock", Issues = new[] { DockerEngineIssue.NotReachable } }
            .ToString().Should().Contain("NotReachable");
}
