using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

public class RustWorkbenchLinuxTests
{
    private const string MinimalManifest = "[package]\nname = \"tiny\"\nversion = \"0.1.0\"\nedition = \"2021\"\n";

    private static readonly string[] ForeignTriples = { LinuxLinkProfile.Arm64Triple, LinuxLinkProfile.RiscV64Triple };

    private static async Task CrossReadyToolchainAsync(RustWorkbench workbench)
    {
        CargoAvailability.SkipIfMissing();
        var toolchain = await workbench.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (toolchain.HostTriple != LinuxLinkProfile.X64Triple)
        {
            Assert.Skip($"The live Linux cross test expects an x86-64 Linux host, not {toolchain.HostTriple}.");
        }

        var missing = ForeignTriples.Where(t => !toolchain.InstalledTargets.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            Assert.Skip("Linux cross targets are not installed: rustup target add " + string.Join(" ", missing));
        }

        var readiness = workbench.EvaluateReadiness(toolchain, ToolchainRequirements.ForLinuxCross(ForeignTriples));
        if (readiness.MissingLinuxCrossTargets.Count > 0)
        {
            Assert.Skip("Linux cross tools are missing: " + string.Join("; ", readiness.Issues.Where(i => i.Kind == ReadinessIssueKind.LinuxCrossToolMissing).Select(i => i.Message)));
        }
    }

    private static async Task<ushort> ReadElfMachineAsync(string path)
    {
        var header = new byte[20];
        await using (var stream = File.OpenRead(path))
        {
            await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
        }

        header.Take(4).Should().Equal((byte)0x7F, (byte)'E', (byte)'L', (byte)'F');
        return BitConverter.ToUInt16(header, 18);
    }

    private static RustWorkbench FakeWorkbench(SampleSandbox sandbox, RustToolchainInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The fake cargo is a /bin/sh script.");
        }

        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var cargo = sandbox.WriteFile("fake-bin/cargo", "#!/bin/sh\necho \"$*\" >> \"$(dirname \"$0\")/ran.txt\"\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(cargo, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new RustWorkbench(new RustWorkbenchOptions { CargoPath = cargo }, (_, _) => Task.FromResult(info));
    }

    private static bool CargoRan(SampleSandbox sandbox) => File.Exists(Path.Combine(sandbox.Directory, "fake-bin", "ran.txt"));

    [Fact]
    public async Task BuildLinuxAsync_builds_sample_13_for_both_foreign_targets_into_the_nuget_runtimes_folders()
    {
        //Arrange
        var workbench = new RustWorkbench();
        await CrossReadyToolchainAsync(workbench);
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();

        //Act
        var result = await workbench.BuildLinuxAsync(
            workbench.OpenProject(sandbox.Directory), output.Directory,
            new CargoBuildOptions { Profile = CargoProfile.Release }, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString() + Environment.NewLine + string.Join(Environment.NewLine, result.Builds.Values.Select(b => b.StandardError)));
        result.Layout.Should().Be(LinuxOutputLayout.NuGetRuntimes);
        result.Builds.Keys.Should().Equal("aarch64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");
        result.Files.Select(f => Path.GetRelativePath(output.Directory, f.Path).Replace('\\', '/')).Should().Equal(
            "runtimes/linux-arm64/native/libtest_project_13.so",
            "runtimes/linux-riscv64/native/libtest_project_13.so");
        result.Files.Should().OnlyContain(f => !f.IsExecutable);
        (await ReadElfMachineAsync(result.Files[0].Path)).Should().Be(0xB7, "linux-arm64 is AArch64");
        (await ReadElfMachineAsync(result.Files[1].Path)).Should().Be(0xF3, "linux-riscv64 is RISC-V");
    }

    [Fact]
    public async Task BuildLinuxAsync_lays_out_triple_folders_with_programs_when_asked()
    {
        //Arrange
        var workbench = new RustWorkbench();
        await CrossReadyToolchainAsync(workbench);
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var linuxOptions = new LinuxBuildOptions { Layout = LinuxOutputLayout.ByTriple, IncludeExecutables = true, Profile = LinuxLinkProfile.ForTriple(LinuxLinkProfile.RiscV64Triple) };

        //Act
        var result = await workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), output.Directory, null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Builds.Keys.Should().Equal("riscv64gc-unknown-linux-gnu");
        result.Files.Select(f => Path.GetRelativePath(output.Directory, f.Path).Replace('\\', '/')).Should().BeEquivalentTo(new[]
        {
            "riscv64gc-unknown-linux-gnu/libtest_project_13.so",
            "riscv64gc-unknown-linux-gnu/test_project_13",
        });
        var program = result.Files.Single(f => f.IsExecutable);
        (await ReadElfMachineAsync(program.Path)).Should().Be(0xF3);
        result.Files.Should().OnlyContain(f => f.RuntimeIdentifier == "linux-riscv64" && f.Triple == "riscv64gc-unknown-linux-gnu");
    }

    [Fact]
    public async Task BuildLinuxAsync_throws_with_cross_guidance_before_cargo_runs_when_the_tools_are_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workbench = FakeWorkbench(sandbox, TestToolchains.WithTargets("x86_64-unknown-linux-gnu", "aarch64-unknown-linux-gnu"));
        var linuxOptions = new LinuxBuildOptions { ToolDirectory = sandbox.CreateDirectory("no-cross-tools") };

        //Act
        var act = () => workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), Path.Combine(sandbox.Directory, "out"), null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Readiness.MissingTargets.Should().Equal("riscv64gc-unknown-linux-gnu");
        exception.Readiness.MissingLinuxCrossTargets.Should().Equal("aarch64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");
        exception.Guidance.Select(g => g.Topic).Should().Equal(InstallGuidanceTopic.AddTargets, InstallGuidanceTopic.InstallLinuxCrossTools);
        exception.Guidance[1].Commands.Should().Contain("rustup target add aarch64-unknown-linux-gnu riscv64gc-unknown-linux-gnu");
        CargoRan(sandbox).Should().BeFalse();
    }

    [Fact]
    public async Task BuildLinuxAsync_defaults_to_the_linux_targets_other_than_the_host()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workbench = FakeWorkbench(sandbox, TestToolchains.WithTargets("aarch64-unknown-linux-gnu"));
        var linuxOptions = new LinuxBuildOptions { ToolDirectory = sandbox.CreateDirectory("no-cross-tools") };

        //Act
        var act = () => workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), Path.Combine(sandbox.Directory, "out"), null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Readiness.MissingTargets.Should().Equal("x86_64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");
    }

    [Fact]
    public async Task BuildLinuxAsync_rejects_the_host_triple()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workbench = FakeWorkbench(sandbox, TestToolchains.Complete());
        var linuxOptions = new LinuxBuildOptions();
        linuxOptions.Triples.Add("x86_64-unknown-linux-gnu");

        //Act
        var act = () => workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), "/out", null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*own target*BuildAsync*");
        CargoRan(sandbox).Should().BeFalse();
    }

    [Fact]
    public async Task BuildLinuxAsync_rejects_a_non_linux_triple()
    {
        //Arrange
        var workbench = new RustWorkbench();
        using var sandbox = SampleSandbox.FromSample(13);
        var linuxOptions = new LinuxBuildOptions();
        linuxOptions.Triples.Add("aarch64-linux-android");

        //Act
        var act = () => workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), "/out", null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*aarch64-linux-android*");
    }

    [Fact]
    public async Task BuildLinuxAsync_rejects_a_profile_that_does_not_fit_a_requested_triple()
    {
        //Arrange
        var workbench = new RustWorkbench();
        using var sandbox = SampleSandbox.FromSample(13);
        var linuxOptions = new LinuxBuildOptions { Profile = LinuxLinkProfile.ForTriple(LinuxLinkProfile.Arm64Triple) };
        linuxOptions.Triples.Add(LinuxLinkProfile.RiscV64Triple);

        //Act
        var act = () => workbench.BuildLinuxAsync(workbench.OpenProject(sandbox.Directory), "/out", null, linuxOptions, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*riscv64-linux-gnu*aarch64-linux-gnu*");
    }

    [Theory]
    [InlineData("aarch64-linux-gnu-gcc", "aarch64-linux-gnu", null)]
    [InlineData("/usr/bin/riscv64-linux-gnu-gcc", "riscv64-linux-gnu", "/usr/bin")]
    public void ProfileFromLinker_reads_a_gnu_cross_gcc(string linker, string prefix, string directory)
    {
        //Act
        var profile = RustWorkbench.ProfileFromLinker(linker);

        //Assert
        profile.ToolPrefix.Should().Be(prefix);
        profile.BinDirectory.Should().Be(directory);
        profile.CompilerPath.Should().Be(linker);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("clang")]
    [InlineData("-gcc")]
    [InlineData("rust-lld")]
    public void ProfileFromLinker_ignores_other_linkers(string linker)
    {
        //Act
        var profile = RustWorkbench.ProfileFromLinker(linker);

        //Assert
        profile.Should().BeNull();
    }
}
