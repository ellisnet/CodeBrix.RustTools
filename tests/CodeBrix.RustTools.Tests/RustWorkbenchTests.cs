using System;
using System.Net;
using System.Threading.Tasks;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

public class RustWorkbenchTests
{
    [Fact]
    public void can_create_with_default_options()
    {
        //Act
        var workbench = new RustWorkbench();

        //Assert
        workbench.Options.Should().NotBeNull();
        workbench.Options.EffectiveUserAgent.Should().Be(RustWorkbenchOptions.DefaultUserAgent);
        workbench.Options.ToolProbeTimeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task DetectToolchainAsync_detects_cargo_on_this_machine()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var workbench = new RustWorkbench();

        //Act
        var info = await workbench.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        info.Cargo.IsAvailable.Should().BeTrue();
        info.ToString().Should().Contain("Cargo: Installed");
    }

    [Fact]
    public async Task DetectToolchainAsync_then_EvaluateReadiness_is_ready_on_this_machine()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var workbench = new RustWorkbench();
        var info = await workbench.DetectToolchainAsync(RepoPaths.Sample(5), TestContext.Current.CancellationToken);

        //Act
        var readiness = workbench.EvaluateReadiness(info);

        //Assert
        readiness.IsReady.Should().BeTrue(readiness.ToString());
        workbench.GetInstallGuidance(readiness).Should().BeEmpty();
    }

    [Fact]
    public void GetInstallGuidance_explains_how_to_install_missing_rust()
    {
        //Arrange
        var workbench = new RustWorkbench();
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Cargo, "x"), InstalledTool.Missing(RustTool.Rustc, "x"));

        //Act
        var guidance = workbench.GetInstallGuidance(workbench.EvaluateReadiness(info));

        //Assert
        guidance.Should().ContainSingle().Which.Topic.Should().Be(InstallGuidanceTopic.InstallRust);
    }

    [Fact]
    public async Task CheckForToolUpdatesAsync_compares_with_the_stubbed_release()
    {
        //Arrange
        var handler = new StubHttpMessageHandler()
            .Respond("/dist/channel-rust-stable.toml", RepoPaths.ReadFixture("channel-rust-stable.toml"))
            .Respond("/rustup/release-stable.toml", RepoPaths.ReadFixture("rustup-release-stable.toml"));
        var workbench = new RustWorkbench(new RustWorkbenchOptions { HttpMessageHandler = handler });

        //Act
        var report = await workbench.CheckForToolUpdatesAsync(TestToolchains.Complete(), TestContext.Current.CancellationToken);

        //Assert
        report.Errors.Should().BeEmpty();
        report.LatestRelease.RustcVersion.ToString().Should().Be("1.99.0");
        report.LatestRustupVersion.ToString().Should().Be("1.29.1");
        report.AnyUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForToolUpdatesAsync_records_download_failures_instead_of_throwing()
    {
        //Arrange
        var handler = new StubHttpMessageHandler()
            .Respond("/dist/channel-rust-stable.toml", "down", HttpStatusCode.BadGateway)
            .Respond("/rustup/release-stable.toml", RepoPaths.ReadFixture("rustup-release-stable.toml"));
        var workbench = new RustWorkbench(new RustWorkbenchOptions { HttpMessageHandler = handler });

        //Act
        var report = await workbench.CheckForToolUpdatesAsync(TestToolchains.Complete(), TestContext.Current.CancellationToken);

        //Assert
        report.Errors.Should().ContainSingle().Which.Should().StartWith("Latest stable release:");
        report.LatestRelease.Should().BeNull();
        report.GetCheck(RustTool.Rustup).IsUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForToolUpdatesAsync_rejects_a_null_toolchain()
    {
        //Arrange
        var workbench = new RustWorkbench();

        //Act
        Func<Task> act = () => workbench.CheckForToolUpdatesAsync(null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
