using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// The readiness check the compiling verbs of <see cref="RustWorkbench"/> make before cargo
/// runs, fed with synthetic toolchains. The cargo used is a /bin/sh script that records that it
/// ran, so the tests can prove cargo was (or was not) started.
/// </summary>
public class RustWorkbenchReadinessTests
{
    private const string MinimalManifest = "[package]\nname = \"tiny\"\nversion = \"0.1.0\"\nedition = \"2021\"\n";

    private static string WriteRecordingCargo(SampleSandbox sandbox)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The fake cargo is a /bin/sh script.");
        }

        var path = sandbox.WriteFile(
            "fake-bin/cargo",
            "#!/bin/sh\n"
            + "echo \"$*\" >> \"$(dirname \"$0\")/ran.txt\"\n"
            + "exit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static bool CargoRan(string fakeCargo)
        => File.Exists(Path.Combine(Path.GetDirectoryName(fakeCargo), "ran.txt"));

    private static RustWorkbench Workbench(string fakeCargo, RustToolchainInfo info)
        => new RustWorkbench(new RustWorkbenchOptions { CargoPath = fakeCargo }, (_, _) => Task.FromResult(info));

    private static RustToolchainInfo WithCToolchain(OperatingSystemKind os, InstalledTool compiler)
        => TestToolchains.With(os, compiler, new InstalledTool { Tool = RustTool.Linker, Status = compiler.Status, Message = compiler.Message });

    [Theory]
    [InlineData(CargoCommand.Build)]
    [InlineData(CargoCommand.Check)]
    [InlineData(CargoCommand.Test)]
    [InlineData(CargoCommand.Doc)]
    [InlineData(CargoCommand.Clippy)]
    public async Task RunCargoAsync_throws_before_cargo_runs_when_the_C_toolchain_is_missing(CargoCommand command)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var info = WithCToolchain(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.CCompiler, "'cc' was not found."));
        var workbench = Workbench(fakeCargo, info);
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var act = () => workbench.RunCargoAsync(project, command, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Message.Should().Contain("C compiler");
        exception.Readiness.MissingTools.Should().Equal(RustTool.CCompiler);
        exception.Guidance.Should().ContainSingle().Which.Topic.Should().Be(InstallGuidanceTopic.InstallCToolchain);
        CargoRan(fakeCargo).Should().BeFalse();
    }

    [Theory]
    [InlineData(OperatingSystemKind.DebianLinux, "sudo apt install build-essential pkg-config")]
    [InlineData(OperatingSystemKind.MacOS, "xcode-select --install")]
    [InlineData(OperatingSystemKind.Windows, "Microsoft.VisualStudio.Workload.VCTools")]
    public async Task BuildAsync_attaches_the_C_toolchain_guidance_for_the_operating_system(OperatingSystemKind os, string expectedCommand)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var info = WithCToolchain(os, InstalledTool.Missing(RustTool.CCompiler, "not found"));
        var workbench = Workbench(fakeCargo, info);
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var act = () => workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var guidance = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which.Guidance.Single();
        guidance.OperatingSystem.Should().Be(os);
        guidance.Commands.Should().Contain(c => c.Contains(expectedCommand));
        CargoRan(fakeCargo).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_proceeds_and_records_a_warning_when_the_C_toolchain_is_undetermined()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var info = WithCToolchain(OperatingSystemKind.DebianLinux, InstalledTool.Unknown(RustTool.CCompiler, "'cc --version' timed out."));
        var workbench = Workbench(fakeCargo, info);
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        CargoRan(fakeCargo).Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Readiness.IsReady.Should().BeTrue();
        var issue = result.Readiness.Issues.Should().ContainSingle().Which;
        issue.Kind.Should().Be(ReadinessIssueKind.ToolStatusUnknown);
        issue.Tool.Should().Be(RustTool.CCompiler);
        issue.IsBlocking.Should().BeFalse();
    }

    [Fact]
    public async Task CleanAsync_runs_without_a_C_toolchain()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var detections = 0;
        var info = WithCToolchain(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.CCompiler, "not found"));
        var workbench = new RustWorkbench(new RustWorkbenchOptions { CargoPath = fakeCargo }, (_, _) =>
        {
            Interlocked.Increment(ref detections);
            return Task.FromResult(info);
        });
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.CleanAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        CargoRan(fakeCargo).Should().BeTrue();
        result.Readiness.Should().BeNull();
        detections.Should().Be(0);
    }

    [Fact]
    public void EvaluateReadiness_honours_requirements_that_drop_the_C_toolchain()
    {
        //Arrange
        var workbench = new RustWorkbench();
        var info = WithCToolchain(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.CCompiler, "not found"));

        //Act
        var relaxed = workbench.EvaluateReadiness(info, new ToolchainRequirements { RequireCCompiler = false });
        var strict = workbench.EvaluateReadiness(info);

        //Assert
        relaxed.IsReady.Should().BeTrue();
        strict.IsReady.Should().BeFalse();
        strict.MissingTools.Should().Equal(RustTool.CCompiler);
    }

    [Fact]
    public async Task BuildAsync_builds_sample_05_on_this_machine_after_a_ready_check()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.Readiness.IsReady.Should().BeTrue();
        result.Readiness.MissingTools.Should().BeEmpty();
        result.Readiness.Toolchain.GetTool(RustTool.CCompiler).Status.Should().NotBe(ToolStatus.Missing);
    }

    private static string RanLog(string fakeCargo)
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(fakeCargo), "ran.txt"));

    [Fact]
    public async Task BuildAsync_throws_before_cargo_runs_when_a_foreign_linux_target_is_not_installed()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var workbench = Workbench(fakeCargo, TestToolchains.Complete());
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { TargetTriple = "aarch64-unknown-linux-gnu" };

        //Act
        var act = () => workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Message.Should().Contain("aarch64-unknown-linux-gnu");
        exception.Readiness.MissingTargets.Should().Equal("aarch64-unknown-linux-gnu");
        exception.Guidance.Should().ContainSingle().Which.Commands.Should().Equal("rustup target add aarch64-unknown-linux-gnu");
        CargoRan(fakeCargo).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_throws_with_the_debian_packages_when_the_profile_cross_gcc_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var emptyBin = sandbox.CreateDirectory("no-cross-tools");
        var workbench = Workbench(fakeCargo, TestToolchains.WithTargets("x86_64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu"));
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions();
        LinuxLinkProfile.ForTriple("riscv64gc-unknown-linux-gnu", emptyBin).ApplyTo(options, "riscv64gc-unknown-linux-gnu");

        //Act
        var act = () => workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Readiness.MissingTargets.Should().BeEmpty();
        exception.Readiness.MissingLinuxCrossTargets.Should().Equal("riscv64gc-unknown-linux-gnu");
        var guidance = exception.Guidance.Should().ContainSingle().Which;
        guidance.Topic.Should().Be(InstallGuidanceTopic.InstallLinuxCrossTools);
        guidance.Commands.Should().Contain("sudo apt install gcc-riscv64-linux-gnu g++-riscv64-linux-gnu libc6-dev-riscv64-cross");
        CargoRan(fakeCargo).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_runs_cargo_for_the_host_triple_without_a_cross_check()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var workbench = Workbench(fakeCargo, TestToolchains.Complete());
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { TargetTriple = "x86_64-unknown-linux-gnu" };

        //Act
        var result = await workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue();
        CargoRan(fakeCargo).Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_skips_the_target_check_of_a_toolchain_without_rustup()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Rustup, "not found"));
        var workbench = Workbench(fakeCargo, info);
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { TargetTriple = "aarch64-unknown-linux-gnu" };

        //Act
        var result = await workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue();
        CargoRan(fakeCargo).Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_skips_cargo_metadata_without_a_progress_receiver_or_policy()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var workbench = Workbench(fakeCargo, TestToolchains.Complete());
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue();
        RanLog(fakeCargo).Should().NotContain("metadata").And.Contain("build");
    }

    [Fact]
    public async Task BuildAsync_runs_cargo_metadata_for_the_estimate_when_a_progress_reporter_is_passed()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var workbench = Workbench(fakeCargo, TestToolchains.Complete());
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        await workbench.BuildAsync(project, progress: new CollectingProgress(), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        RanLog(fakeCargo).Should().Contain("metadata");
    }

    [Fact]
    public async Task BuildAsync_runs_cargo_metadata_for_the_estimate_when_the_progress_event_has_a_handler()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var fakeCargo = WriteRecordingCargo(sandbox);
        var workbench = Workbench(fakeCargo, TestToolchains.Complete());
        workbench.Progress += (_, _) => { };
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        RanLog(fakeCargo).Should().Contain("metadata");
    }
}
