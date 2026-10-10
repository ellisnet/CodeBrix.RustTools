using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

public class RustWorkbenchMacOSUniversalTests
{
    private static async Task<RustToolchainInfo> MacToolchainAsync(RustWorkbench workbench)
    {
        CargoAvailability.SkipIfMissing();
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Universal macOS builds are exercised on a Mac host.");
        }

        return await workbench.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BuildMacOSUniversalAsync_throws_with_target_guidance_when_a_macos_target_is_missing()
    {
        //Arrange
        var workbench = new RustWorkbench();
        var toolchain = await MacToolchainAsync(workbench);
        var missing = MacOSLinkProfile.Triples.Where(t => !toolchain.InstalledTargets.Contains(t)).ToList();
        if (missing.Count == 0)
        {
            Assert.Skip("Both macOS targets are installed here, so the guidance path cannot be exercised.");
        }

        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();

        //Act
        var act = () => workbench.BuildMacOSUniversalAsync(workbench.OpenProject(sandbox.Directory), output.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<ToolchainNotFoundException>();
        thrown.Which.Readiness.MissingTargets.Should().BeEquivalentTo(missing);
        thrown.Which.Guidance.Should().Contain(g => g.Topic == InstallGuidanceTopic.AddTargets && g.Commands.Any(c => c.Contains(missing[0])));
    }

    [Fact]
    public async Task BuildMacOSUniversalAsync_joins_the_program_and_library_of_sample_13()
    {
        //Arrange
        var workbench = new RustWorkbench();
        var toolchain = await MacToolchainAsync(workbench);
        if (MacOSLinkProfile.Triples.Any(t => !toolchain.InstalledTargets.Contains(t)))
        {
            Assert.Skip("Both macOS targets (rustup target add aarch64-apple-darwin x86_64-apple-darwin) are needed for a universal build.");
        }

        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var options = new CargoBuildOptions { Profile = CargoProfile.Release };

        //Act
        var result = await workbench.BuildMacOSUniversalAsync(workbench.OpenProject(sandbox.Directory), output.Directory, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Arm64Build.Success.Should().BeTrue(result.Arm64Build.StandardError);
        result.X64Build.Success.Should().BeTrue(result.X64Build.StandardError);
        result.Files.Select(f => f.FileName).Should().BeEquivalentTo(new[] { "test_project_13", "libtest_project_13.dylib" });
        foreach (var file in result.Files)
        {
            file.Slices.Select(s => s.Architecture).Should().Equal("x86_64", "arm64");
            MacOSUniversalBinary.ReadSlices(file.Path).Should().HaveCount(2);
        }

        var program = result.Files.Single(f => f.IsExecutable);
        var run = Process.Start(new ProcessStartInfo(program.Path) { RedirectStandardOutput = true, RedirectStandardError = true });
        var stdout = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await run.WaitForExitAsync(TestContext.Current.CancellationToken);
        run.ExitCode.Should().Be(0);
        stdout.Should().Contain("cb_add(2, 3) = 5");
    }
}
