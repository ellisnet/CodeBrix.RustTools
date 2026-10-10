using System;
using CodeBrix.RustTools.Building;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class LinuxLinkProfileTests
{
    [Theory]
    [InlineData("x86_64-unknown-linux-gnu", "x86_64-linux-gnu", "linux-x64", "gcc-x86-64-linux-gnu g++-x86-64-linux-gnu libc6-dev-amd64-cross")]
    [InlineData("aarch64-unknown-linux-gnu", "aarch64-linux-gnu", "linux-arm64", "gcc-aarch64-linux-gnu g++-aarch64-linux-gnu libc6-dev-arm64-cross")]
    [InlineData("riscv64gc-unknown-linux-gnu", "riscv64-linux-gnu", "linux-riscv64", "gcc-riscv64-linux-gnu g++-riscv64-linux-gnu libc6-dev-riscv64-cross")]
    public void Table_names_the_prefix_rid_and_debian_packages_of_each_target(string triple, string prefix, string rid, string packages)
    {
        LinuxLinkProfile.IsLinuxTriple(triple).Should().BeTrue();
        LinuxLinkProfile.ToolPrefixOf(triple).Should().Be(prefix);
        LinuxLinkProfile.RuntimeIdentifierOf(triple).Should().Be(rid);
        string.Join(" ", LinuxLinkProfile.DebianPackagesOf(triple)).Should().Be(packages);
        LinuxLinkProfile.ForTriple(triple).ToolPrefix.Should().Be(prefix);
    }

    [Fact]
    public void EnvironmentFor_sets_the_cross_gcc_as_linker_and_c_compiler_and_the_cross_ar_as_archiver()
    {
        //Act
        var environment = LinuxLinkProfile.ForTriple("aarch64-unknown-linux-gnu").EnvironmentFor("aarch64-unknown-linux-gnu");

        //Assert
        environment["CC_aarch64_unknown_linux_gnu"].Should().Be("aarch64-linux-gnu-gcc");
        environment["CXX_aarch64_unknown_linux_gnu"].Should().Be("aarch64-linux-gnu-g++");
        environment["AR_aarch64_unknown_linux_gnu"].Should().Be("aarch64-linux-gnu-ar");
        environment["RANLIB_aarch64_unknown_linux_gnu"].Should().Be("aarch64-linux-gnu-ranlib");
        environment["CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER"].Should().Be("aarch64-linux-gnu-gcc");
        environment["CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_AR"].Should().Be("aarch64-linux-gnu-ar");
        environment.Count.Should().Be(6);
    }

    [Fact]
    public void EnvironmentFor_joins_a_bin_folder_with_its_own_separator()
    {
        //Assert
        new LinuxLinkProfile("riscv64-linux-gnu", "/opt/cross/bin/").EnvironmentFor("riscv64gc-unknown-linux-gnu")["CARGO_TARGET_RISCV64GC_UNKNOWN_LINUX_GNU_LINKER"].Should().Be("/opt/cross/bin/riscv64-linux-gnu-gcc");
        new LinuxLinkProfile("x86_64-linux-gnu", @"C:\cross\bin").CompilerPath.Should().Be(@"C:\cross\bin\x86_64-linux-gnu-gcc");
    }

    [Fact]
    public void EnvironmentFor_rejects_a_triple_of_another_prefix_or_platform()
    {
        //Act
        var wrongPrefix = () => LinuxLinkProfile.ForTriple("x86_64-unknown-linux-gnu").EnvironmentFor("aarch64-unknown-linux-gnu");
        var notLinux = () => LinuxLinkProfile.ForTriple("x86_64-unknown-linux-gnu").EnvironmentFor("aarch64-apple-darwin");
        var unknown = () => LinuxLinkProfile.ToolPrefixOf("x86_64-unknown-linux-musl");

        //Assert
        wrongPrefix.Should().Throw<ArgumentException>().WithMessage("*aarch64-linux-gnu*x86_64-linux-gnu*");
        notLinux.Should().Throw<ArgumentException>();
        unknown.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyTo_sets_the_target_and_the_environment()
    {
        //Arrange
        var options = new CargoBuildOptions();

        //Act
        LinuxLinkProfile.ForTriple("riscv64gc-unknown-linux-gnu").ApplyTo(options, "riscv64gc-unknown-linux-gnu");

        //Assert
        options.TargetTriple.Should().Be("riscv64gc-unknown-linux-gnu");
        options.Environment["CC_riscv64gc_unknown_linux_gnu"].Should().Be("riscv64-linux-gnu-gcc");
    }

    [Fact]
    public void Constructor_requires_a_prefix()
    {
        //Act
        var act = () => new LinuxLinkProfile(" ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
