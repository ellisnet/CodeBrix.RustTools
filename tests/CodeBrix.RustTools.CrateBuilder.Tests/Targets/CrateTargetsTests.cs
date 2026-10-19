using System;
using System.Linq;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Targets;

public class CrateTargetsTests
{
    [Theory]
    [InlineData(CrateTarget.WinX64, "win-x64", "x86_64-pc-windows-gnullvm", "my_crate.dll")]
    [InlineData(CrateTarget.WinArm64, "win-arm64", "aarch64-pc-windows-gnullvm", "my_crate.dll")]
    [InlineData(CrateTarget.OsxX64, "osx-x64", "x86_64-apple-darwin", "libmy_crate.dylib")]
    [InlineData(CrateTarget.OsxArm64, "osx-arm64", "aarch64-apple-darwin", "libmy_crate.dylib")]
    [InlineData(CrateTarget.LinuxX64, "linux-x64", "x86_64-unknown-linux-gnu", "libmy_crate.so")]
    [InlineData(CrateTarget.LinuxArm64, "linux-arm64", "aarch64-unknown-linux-gnu", "libmy_crate.so")]
    [InlineData(CrateTarget.LinuxRiscV64, "linux-riscv64", "riscv64gc-unknown-linux-gnu", "libmy_crate.so")]
    [InlineData(CrateTarget.AndroidX64, "android-x64", "x86_64-linux-android", "libmy_crate.so")]
    [InlineData(CrateTarget.AndroidArm64, "android-arm64", "aarch64-linux-android", "libmy_crate.so")]
    public void each_target_maps_to_its_rid_triple_and_file_name(CrateTarget target, string rid, string triple, string fileName)
    {
        //Act
        var actualRid = CrateTargets.Rid(target);
        var actualTriple = CrateTargets.Triple(target);
        var actualFile = CrateTargets.NativeFileName(target, "my-crate");

        //Assert
        actualRid.Should().Be(rid);
        actualTriple.Should().Be(triple);
        actualFile.Should().Be(fileName);
        CrateTargets.FromRid(rid).Should().Be(target);
    }

    [Fact]
    public void FromRid_round_trips_every_target_and_ignores_case()
    {
        //Act
        var roundTripped = CrateTargets.All.Select(t => CrateTargets.FromRid(CrateTargets.Rid(t).ToUpperInvariant())).ToList();

        //Assert
        roundTripped.Should().Equal(CrateTargets.All);
    }

    [Fact]
    public void FromRid_rejects_an_unknown_rid()
    {
        //Act
        var act = () => CrateTargets.FromRid("osx");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*not a runtime identifier*");
    }

    [Fact]
    public void the_triples_are_the_docker_package_triples()
    {
        //Act
        var mismatches = CrateTargets.All
            .Where(t => CrateTargets.Triple(t) != CrossTargetInfo.Get(CrateTargets.ToCrossTarget(t)).Triple)
            .ToList();

        //Assert
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void the_groups_cover_every_target_once()
    {
        //Act
        var grouped = CrateTargets.MacOS.Concat(CrateTargets.Windows).Concat(CrateTargets.Linux).Concat(CrateTargets.Android).ToList();

        //Assert
        grouped.Should().HaveCount(9);
        grouped.Should().OnlyHaveUniqueItems();
        CrateTargets.All.Should().HaveCount(9);
        grouped.All(t => CrateTargets.All.Contains(t)).Should().BeTrue();
    }

    [Fact]
    public void the_group_predicates_agree_with_the_groups()
    {
        //Act
        var macOS = CrateTargets.All.Where(CrateTargets.IsMacOS).ToList();
        var windows = CrateTargets.All.Where(CrateTargets.IsWindows).ToList();
        var linux = CrateTargets.All.Where(CrateTargets.IsLinux).ToList();
        var android = CrateTargets.All.Where(CrateTargets.IsAndroid).ToList();

        //Assert
        macOS.Should().Equal(CrateTargets.MacOS);
        windows.Should().Equal(CrateTargets.Windows);
        linux.Should().Equal(CrateTargets.Linux);
        android.Should().Equal(CrateTargets.Android);
    }

    [Fact]
    public void NativeFileName_requires_a_crate_name()
    {
        //Act
        var act = () => CrateTargets.NativeFileName(CrateTarget.LinuxX64, " ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
