using System;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class ContainerCargoRunnerTests
{
    [Fact]
    public void CreateInvocation_runs_cargo_under_setsid_with_json_messages_in_work()
    {
        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxX64, null, "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CommandLine.Should().Equal("setsid", "-w", "cargo", "build", "--message-format=json");
        invocation.CargoArguments.Should().Equal("build", "--message-format=json");
        invocation.DisplayCommandLine.Should().Be("cargo build --message-format=json");
        invocation.WorkingDirectory.Should().Be("/work");
    }

    [Fact]
    public void CreateInvocation_sets_the_container_environment_and_a_unique_marker()
    {
        //Act
        var first = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxX64, "/work", "x86_64-unknown-linux-gnu");
        var second = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxX64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        first.Environment.Should().Contain("CARGO_TARGET_DIR=/work/target");
        first.Environment.Should().Contain("CARGO_TERM_COLOR=never");
        first.Environment.Should().Contain("RUSTUP_AUTO_INSTALL=0");
        first.MarkerEntry.Should().MatchRegex("^CODEBRIX_RUSTTOOLS_RUN=[0-9a-f]{32}$");
        first.Environment.Should().Contain(first.MarkerEntry);
        second.MarkerEntry.Should().NotBe(first.MarkerEntry);
    }

    [Fact]
    public void CreateInvocation_builds_the_same_arguments_as_the_host_runner()
    {
        //Arrange
        var options = new CargoBuildOptions { Profile = CargoProfile.Release, Locked = true, Jobs = 2 };
        options.Features.Add("json");
        options.Packages.Add("unitconv_cli");

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, options, CrossTarget.LinuxX64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().Equal("build", "--message-format=json", "--release", "--features", "json", "-p", "unitconv_cli", "--locked", "-j", "2");
    }

    [Fact]
    public void CreateInvocation_keeps_the_options_target_triple_for_the_container_target()
    {
        //Arrange
        var options = new CargoBuildOptions { TargetTriple = "x86_64-unknown-linux-musl" };

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, options, CrossTarget.LinuxX64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().ContainInOrder("--target", "x86_64-unknown-linux-musl");
    }

    [Theory]
    [InlineData(CrossTarget.WindowsX64Gnullvm, "x86_64-pc-windows-gnullvm", "x86_64", "X86_64_PC_WINDOWS_GNULLVM", "x86_64_pc_windows_gnullvm")]
    [InlineData(CrossTarget.WindowsArm64Gnullvm, "aarch64-pc-windows-gnullvm", "aarch64", "AARCH64_PC_WINDOWS_GNULLVM", "aarch64_pc_windows_gnullvm")]
    public void CreateInvocation_points_windows_builds_at_llvm_mingw_with_a_static_unwinder(CrossTarget target, string triple, string machine, string upper, string lower)
    {
        //Arrange
        var options = new CargoBuildOptions();

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, options, target, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().ContainInOrder("--target", triple);
        invocation.Environment.Should().Contain($"CARGO_TARGET_{upper}_LINKER=/opt/llvm-mingw/bin/{machine}-w64-mingw32-clang");
        invocation.Environment.Should().Contain($"CARGO_TARGET_{upper}_AR=/opt/llvm-mingw/bin/llvm-ar");
        invocation.Environment.Should().Contain($"CARGO_TARGET_{upper}_RUSTFLAGS=-C target-feature=+crt-static");
        invocation.Environment.Should().Contain($"CC_{lower}=/opt/llvm-mingw/bin/{machine}-w64-mingw32-clang");
        invocation.Environment.Should().Contain($"CXX_{lower}=/opt/llvm-mingw/bin/{machine}-w64-mingw32-clang++");
        invocation.Environment.Should().Contain($"AR_{lower}=/opt/llvm-mingw/bin/llvm-ar");
        options.TargetTriple.Should().BeNull();
    }

    [Fact]
    public void CreateInvocation_points_android_builds_at_the_ndk_clang_for_api_level_24()
    {
        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.AndroidArm64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        const string Bin = "/opt/android-ndk/toolchains/llvm/prebuilt/linux-x86_64/bin";
        invocation.CargoArguments.Should().ContainInOrder("--target", "aarch64-linux-android");
        invocation.Environment.Should().Contain($"CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER={Bin}/aarch64-linux-android24-clang");
        invocation.Environment.Should().Contain($"CC_aarch64_linux_android={Bin}/aarch64-linux-android24-clang");
        invocation.Environment.Should().Contain($"CXX_aarch64_linux_android={Bin}/aarch64-linux-android24-clang++");
        invocation.Environment.Should().Contain($"AR_aarch64_linux_android={Bin}/llvm-ar");
        invocation.Environment.Should().Contain($"CARGO_TARGET_AARCH64_LINUX_ANDROID_AR={Bin}/llvm-ar");
    }

    [Fact]
    public void AndroidEnvironment_names_the_x86_64_tools_for_the_x64_target()
        => ContainerCargoRunner.AndroidEnvironment(CrossTargetInfo.Get(CrossTarget.AndroidX64))
            .Should().Contain("CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER=/opt/android-ndk/toolchains/llvm/prebuilt/linux-x86_64/bin/x86_64-linux-android24-clang");

    [Fact]
    public void CreateInvocation_appends_the_options_environment_and_ignores_null_values()
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.Environment["RUSTFLAGS"] = "-C debuginfo=0";
        options.Environment["REMOVED"] = null;

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, options, CrossTarget.LinuxX64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.Environment.Should().Contain("RUSTFLAGS=-C debuginfo=0");
        invocation.Environment.Should().NotContain(e => e.StartsWith("REMOVED", StringComparison.Ordinal));
        invocation.Environment.Last().Should().Be(invocation.MarkerEntry);
    }

    [Fact]
    public void CreateInvocation_applies_the_macos_link_profile_with_the_install_name()
    {
        //Arrange
        var options = new CargoBuildOptions { MacOSInstallName = "@rpath/libtest_project_13.dylib" };

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, options, CrossTarget.MacOSArm64, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().ContainInOrder("--target", "aarch64-apple-darwin");
        invocation.Environment.Should().Contain("CARGO_TARGET_AARCH64_APPLE_DARWIN_LINKER=/opt/codebrix/bin/rust-lld");
        invocation.Environment.Should().Contain(
            "CARGO_TARGET_AARCH64_APPLE_DARWIN_RUSTFLAGS=-C linker-flavor=ld64.lld -C link-arg=-S --remap-path-prefix=/usr/local/cargo/registry/src=/cargo-registry-src -C link-arg=-install_name -C link-arg=@rpath/libtest_project_13.dylib");
        invocation.Environment.Should().Contain("SDKROOT=/opt/codebrix/macos-sysroot");
        invocation.Environment.Should().Contain("MACOSX_DEPLOYMENT_TARGET=11.0");
        invocation.Environment.Should().NotContain(e => e.StartsWith("CC_", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateInvocation_builds_the_engine_s_own_linux_target_natively_without_a_target_flag()
    {
        //Act
        var explicitNative = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxArm64, "/work", "aarch64-unknown-linux-gnu");
        var resolvedNative = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxNative, "/work", "aarch64-unknown-linux-gnu");

        //Assert
        explicitNative.Target.Should().Be(CrossTarget.LinuxArm64);
        explicitNative.CargoArguments.Should().NotContain("--target");
        explicitNative.Environment.Should().NotContain(e => e.StartsWith("CC_", StringComparison.Ordinal));
        resolvedNative.Target.Should().Be(CrossTarget.LinuxArm64);
        resolvedNative.CargoArguments.Should().Equal("build", "--message-format=json");
    }

    [Theory]
    [InlineData(CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu", "aarch64-linux-gnu", "AARCH64_UNKNOWN_LINUX_GNU", "aarch64_unknown_linux_gnu")]
    [InlineData(CrossTarget.LinuxRiscV64, "riscv64gc-unknown-linux-gnu", "riscv64-linux-gnu", "RISCV64GC_UNKNOWN_LINUX_GNU", "riscv64gc_unknown_linux_gnu")]
    public void CreateInvocation_cross_compiles_a_foreign_linux_target_with_debian_s_cross_gcc(CrossTarget target, string triple, string prefix, string upper, string lower)
    {
        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, target, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().ContainInOrder("--target", triple);
        invocation.Environment.Should().Contain($"CARGO_TARGET_{upper}_LINKER={prefix}-gcc");
        invocation.Environment.Should().Contain($"CARGO_TARGET_{upper}_AR={prefix}-ar");
        invocation.Environment.Should().Contain($"CC_{lower}={prefix}-gcc");
        invocation.Environment.Should().Contain($"CXX_{lower}={prefix}-g++");
        invocation.Environment.Should().Contain($"AR_{lower}={prefix}-ar");
        invocation.Environment.Should().Contain($"RANLIB_{lower}={prefix}-ranlib");
    }

    [Fact]
    public void CreateInvocation_cross_compiles_x64_on_an_arm64_engine()
    {
        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxX64, "/work", "aarch64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().ContainInOrder("--target", "x86_64-unknown-linux-gnu");
        invocation.Environment.Should().Contain("CARGO_TARGET_X86_64_UNKNOWN_LINUX_GNU_LINKER=x86_64-linux-gnu-gcc");
    }

    [Fact]
    public void CreateInvocation_cannot_resolve_LinuxNative_without_an_engine_architecture()
    {
        //Act
        var act = () => ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.LinuxNative, "/work", null);

        //Assert
        act.Should().Throw<CrossTargetNotSupportedException>().Which.Target.Should().Be(CrossTarget.LinuxNative);
    }

    [Fact]
    public void CreateInvocation_rejects_the_universal_pseudo_target()
    {
        //Act
        var act = () => ContainerCargoRunner.CreateInvocation(CargoCommand.Build, null, CrossTarget.MacOSUniversal, "/work", "x86_64-unknown-linux-gnu");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*MacOSUniversal*");
    }

    [Fact]
    public void MacOSEnvironment_omits_the_install_name_when_none_is_set()
        => ContainerCargoRunner.MacOSEnvironment(CrossTargetInfo.Get(CrossTarget.MacOSX64), null)
            .Should().Contain("CARGO_TARGET_X86_64_APPLE_DARWIN_RUSTFLAGS=-C linker-flavor=ld64.lld -C link-arg=-S --remap-path-prefix=/usr/local/cargo/registry/src=/cargo-registry-src");

    [Fact]
    public void CreateInvocation_builds_a_test_command_with_the_test_options()
    {
        //Arrange
        var options = new CargoBuildOptions { NoFailFast = true, TestNameFilter = "adds" };

        //Act
        var invocation = ContainerCargoRunner.CreateInvocation(CargoCommand.Test, options, CrossTarget.LinuxX64, "/work/member", "x86_64-unknown-linux-gnu");

        //Assert
        invocation.CargoArguments.Should().Equal("test", "--message-format=json", "--no-fail-fast", "adds");
        invocation.WorkingDirectory.Should().Be("/work/member");
    }

    [Fact]
    public void KillCommand_passes_the_marker_as_an_argument_to_a_proc_scan()
    {
        //Act
        var command = ContainerCargoRunner.KillCommand("CODEBRIX_RUSTTOOLS_RUN=abc");

        //Assert
        command[0].Should().Be("sh");
        command[1].Should().Be("-c");
        command[2].Should().Contain("/proc/[0-9]*");
        command[2].Should().Contain("kill -KILL");
        command.Last().Should().Be("CODEBRIX_RUSTTOOLS_RUN=abc");
    }
}
