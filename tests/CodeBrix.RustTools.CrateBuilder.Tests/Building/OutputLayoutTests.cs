using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class OutputLayoutTests
{
    [Theory]
    [InlineData(CrateTarget.LinuxRiscV64, "runtimes/linux-riscv64/native")]
    [InlineData(CrateTarget.WinArm64, "runtimes/win-arm64/native")]
    [InlineData(CrateTarget.OsxX64, "runtimes/osx-x64/native")]
    [InlineData(CrateTarget.AndroidArm64, "runtimes/android-arm64/native")]
    public void NuGetRuntimes_is_the_rid_folder(CrateTarget target, string expected) =>
        OutputLayout.RelativeDirectory(ArtifactLayout.NuGetRuntimes, target).Should().Be(expected);

    [Fact]
    public void Flat_is_the_output_folder_itself() =>
        OutputLayout.DirectoryFor("/out/native", ArtifactLayout.Flat, CrateTarget.LinuxX64).Should().Be(Path.GetFullPath("/out/native"));

    [Fact]
    public void ByTarget_is_not_a_crate_builder_layout() =>
        ((Action)(() => OutputLayout.RelativeDirectory(ArtifactLayout.ByTarget, CrateTarget.LinuxX64))).Should().Throw<ArgumentOutOfRangeException>();
}
