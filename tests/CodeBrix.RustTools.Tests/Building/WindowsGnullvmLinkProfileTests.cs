using System;
using CodeBrix.RustTools.Building;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class WindowsGnullvmLinkProfileTests
{
    private const string Bin = "/opt/llvm-mingw/bin";

    private const string Root = "/opt/llvm-mingw";

    [Theory]
    [InlineData("x86_64-pc-windows-gnullvm", "x86_64", "win-x64")]
    [InlineData("aarch64-pc-windows-gnullvm", "aarch64", "win-arm64")]
    public void Table_names_the_machine_and_rid_of_each_target(string triple, string machine, string rid)
    {
        WindowsGnullvmLinkProfile.IsWindowsGnullvmTriple(triple).Should().BeTrue();
        WindowsGnullvmLinkProfile.MachineOf(triple).Should().Be(machine);
        WindowsGnullvmLinkProfile.RuntimeIdentifierOf(triple).Should().Be(rid);
        new WindowsGnullvmLinkProfile(Bin).ClangPath(triple).Should().Be(Bin + "/" + machine + "-w64-mingw32-clang");
    }

    [Fact]
    public void EnvironmentFor_sets_clang_as_linker_and_compiler_llvm_ar_as_archiver_and_the_static_unwinder_flag()
    {
        //Act
        var environment = new WindowsGnullvmLinkProfile(Bin + "/").EnvironmentFor("aarch64-pc-windows-gnullvm");

        //Assert
        environment["CC_aarch64_pc_windows_gnullvm"].Should().Be(Bin + "/aarch64-w64-mingw32-clang");
        environment["CXX_aarch64_pc_windows_gnullvm"].Should().Be(Bin + "/aarch64-w64-mingw32-clang++");
        environment["AR_aarch64_pc_windows_gnullvm"].Should().Be(Bin + "/llvm-ar");
        environment["RANLIB_aarch64_pc_windows_gnullvm"].Should().Be(Bin + "/llvm-ranlib");
        environment["CARGO_TARGET_AARCH64_PC_WINDOWS_GNULLVM_LINKER"].Should().Be(Bin + "/aarch64-w64-mingw32-clang");
        environment["CARGO_TARGET_AARCH64_PC_WINDOWS_GNULLVM_AR"].Should().Be(Bin + "/llvm-ar");
        environment["CARGO_TARGET_AARCH64_PC_WINDOWS_GNULLVM_RUSTFLAGS"].Should().Be("-C target-feature=+crt-static");
        environment["BINDGEN_EXTRA_CLANG_ARGS_aarch64_pc_windows_gnullvm"].Should().Be("--target=aarch64-w64-mingw32 --sysroot=" + Root);
        environment.Count.Should().Be(8);
    }

    [Fact]
    public void SysrootDirectory_is_the_llvm_mingw_folder()
        => new WindowsGnullvmLinkProfile(Bin + "/").SysrootDirectory.Should().Be(Root);

    [Fact]
    public void BindgenClangArguments_targets_mingw_with_forward_slashes_on_a_windows_host()
        => new WindowsGnullvmLinkProfile(@"C:\llvm-mingw\bin", windowsFileNames: true).BindgenClangArguments("x86_64-pc-windows-gnullvm")
            .Should().Be("--target=x86_64-w64-mingw32 --sysroot=C:/llvm-mingw");

    [Fact]
    public void EnvironmentFor_uses_exe_names_on_a_windows_host()
        => new WindowsGnullvmLinkProfile(@"C:\llvm-mingw\bin", windowsFileNames: true).EnvironmentFor("x86_64-pc-windows-gnullvm")["CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER"]
            .Should().Be(@"C:\llvm-mingw\bin\x86_64-w64-mingw32-clang.exe");

    [Fact]
    public void EnvironmentFor_rejects_other_windows_and_non_windows_triples()
    {
        //Act
        var msvc = () => new WindowsGnullvmLinkProfile(Bin).EnvironmentFor("x86_64-pc-windows-msvc");
        var linux = () => WindowsGnullvmLinkProfile.MachineOf("x86_64-unknown-linux-gnu");

        //Assert
        msvc.Should().Throw<ArgumentException>().WithMessage("*x86_64-pc-windows-msvc*");
        linux.Should().Throw<ArgumentException>();
        WindowsGnullvmLinkProfile.IsWindowsGnullvmTriple("x86_64-pc-windows-gnu").Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_sets_the_target_and_the_environment()
    {
        //Arrange
        var options = new CargoBuildOptions();

        //Act
        new WindowsGnullvmLinkProfile(Bin).ApplyTo(options, "X86_64-PC-WINDOWS-GNULLVM");

        //Assert
        options.TargetTriple.Should().Be("x86_64-pc-windows-gnullvm");
        options.Environment["CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_RUSTFLAGS"].Should().Be(WindowsGnullvmLinkProfile.RustFlags);
    }

    [Fact]
    public void Constructor_requires_the_bin_folder()
    {
        //Act
        var act = () => new WindowsGnullvmLinkProfile(" ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
