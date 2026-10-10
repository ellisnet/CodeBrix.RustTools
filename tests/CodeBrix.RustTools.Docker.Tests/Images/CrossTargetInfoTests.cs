using System;
using System.Linq;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Images;

public class CrossTargetInfoTests
{
    [Theory]
    [InlineData(CrossTarget.LinuxNative, "", "", "", ".so", true, false)]
    [InlineData(CrossTarget.LinuxX64, "x86_64-unknown-linux-gnu", "linux-x64", "", ".so", true, true)]
    [InlineData(CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu", "linux-arm64", "", ".so", true, true)]
    [InlineData(CrossTarget.LinuxRiscV64, "riscv64gc-unknown-linux-gnu", "linux-riscv64", "", ".so", true, true)]
    [InlineData(CrossTarget.WindowsX64Gnullvm, "x86_64-pc-windows-gnullvm", "win-x64", ".exe", ".dll", true, true)]
    [InlineData(CrossTarget.WindowsArm64Gnullvm, "aarch64-pc-windows-gnullvm", "win-arm64", ".exe", ".dll", true, true)]
    [InlineData(CrossTarget.AndroidArm64, "aarch64-linux-android", "android-arm64", "", ".so", true, true)]
    [InlineData(CrossTarget.AndroidX64, "x86_64-linux-android", "android-x64", "", ".so", true, true)]
    [InlineData(CrossTarget.MacOSArm64, "aarch64-apple-darwin", "osx-arm64", "", ".dylib", true, true)]
    [InlineData(CrossTarget.MacOSX64, "x86_64-apple-darwin", "osx-x64", "", ".dylib", true, true)]
    [InlineData(CrossTarget.MacOSUniversal, "universal-apple-darwin", "osx", "", ".dylib", true, true)]
    public void Get_describes_each_target(CrossTarget target, string triple, string rid, string exe, string shared, bool supported, bool layer)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.Target.Should().Be(target);
        info.Triple.Should().Be(triple);
        info.RuntimeIdentifier.Should().Be(rid);
        info.ExecutableExtension.Should().Be(exe);
        info.SharedLibraryExtension.Should().Be(shared);
        info.StaticLibraryExtension.Should().Be(".a");
        info.IsSupported.Should().Be(supported);
        info.RequiresLayer.Should().Be(layer);
        info.Notes.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("x86_64", CrossTarget.LinuxX64)]
    [InlineData("amd64", CrossTarget.LinuxX64)]
    [InlineData("aarch64", CrossTarget.LinuxArm64)]
    [InlineData("riscv64", CrossTarget.LinuxRiscV64)]
    public void NativeLinuxTargetFor_and_Resolve_follow_the_engine_architecture(string architecture, CrossTarget expected)
    {
        //Arrange
        var engine = new DockerEngineInfo { Architecture = architecture };

        //Assert
        CrossTargetInfo.NativeLinuxTargetFor(engine).Should().Be(expected);
        CrossTargetInfo.Resolve(CrossTarget.LinuxNative, engine).Should().Be(expected);
        CrossTargetInfo.Resolve(CrossTarget.WindowsX64Gnullvm, engine).Should().Be(CrossTarget.WindowsX64Gnullvm);
        CrossTargetInfo.Get(expected).IsNativeOn(engine).Should().BeTrue();
        CrossTargetInfo.All.Where(i => i.IsLinux && i.Target != expected).Should().OnlyContain(i => !i.IsNativeOn(engine));
    }

    [Fact]
    public void Resolve_refuses_LinuxNative_on_an_engine_without_a_linux_target()
    {
        //Act
        var act = () => CrossTargetInfo.Resolve(CrossTarget.LinuxNative, new DockerEngineInfo { Architecture = "s390x" });

        //Assert
        CrossTargetInfo.NativeLinuxTargetFor(new DockerEngineInfo { Architecture = "s390x" }).Should().BeNull();
        act.Should().Throw<CrossTargetNotSupportedException>().Which.Message.Should().Contain("s390x");
    }

    [Theory]
    [InlineData(CrossTarget.LinuxX64, "x86_64-linux-gnu", "gcc-x86-64-linux-gnu g++-x86-64-linux-gnu libc6-dev-amd64-cross")]
    [InlineData(CrossTarget.LinuxArm64, "aarch64-linux-gnu", "gcc-aarch64-linux-gnu g++-aarch64-linux-gnu libc6-dev-arm64-cross")]
    [InlineData(CrossTarget.LinuxRiscV64, "riscv64-linux-gnu", "gcc-riscv64-linux-gnu g++-riscv64-linux-gnu libc6-dev-riscv64-cross")]
    public void Linux_targets_name_their_gnu_prefix_and_debian_cross_packages(CrossTarget target, string prefix, string packages)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.IsLinux.Should().BeTrue();
        info.IsLinuxNative.Should().BeFalse();
        info.GnuToolPrefix.Should().Be(prefix);
        string.Join(" ", info.DebianCrossPackages).Should().Be(packages);
        CrossTargetInfo.Get(CrossTarget.WindowsX64Gnullvm).GnuToolPrefix.Should().BeNull();
        CrossTargetInfo.Get(CrossTarget.LinuxNative).IsLinuxNative.Should().BeTrue();
    }

    [Theory]
    [InlineData(CrossTarget.WindowsX64Gnullvm)]
    [InlineData(CrossTarget.WindowsArm64Gnullvm)]
    public void Windows_targets_describe_llvm_mingw_and_the_ucrt(CrossTarget target)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.IsWindows.Should().BeTrue();
        info.Notes.Should().Contain("llvm-mingw").And.Contain("Universal C Runtime").And.Contain("No Microsoft SDK");
        CrossTargetInfo.Get(CrossTarget.LinuxX64).IsWindows.Should().BeFalse();
    }

    [Fact]
    public void All_lists_every_enum_value_once_in_order()
        => CrossTargetInfo.All.Select(info => info.Target).Should().Equal(Enum.GetValues<CrossTarget>());

    [Fact]
    public void MacOSUniversal_is_made_of_arm64_then_x64_and_is_not_a_cargo_target()
    {
        //Assert
        CrossTargetInfo.MacOSUniversalSlices.Should().Equal(CrossTarget.MacOSArm64, CrossTarget.MacOSX64);
        CrossTargetInfo.Get(CrossTarget.MacOSUniversal).IsMacOSUniversal.Should().BeTrue();
        CrossTargetInfo.Get(CrossTarget.MacOSArm64).IsMacOSUniversal.Should().BeFalse();
        CrossTargetInfo.Get(CrossTarget.MacOSUniversal).Notes.Should().Contain("runtimes/osx/native");
    }

    [Theory]
    [InlineData(CrossTarget.MacOSArm64)]
    [InlineData(CrossTarget.MacOSX64)]
    [InlineData(CrossTarget.MacOSUniversal)]
    public void macos_targets_carry_the_development_only_statement(CrossTarget target)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.Notes.Should().Contain("must not be distributed");
        info.Notes.Should().Contain("rust-lld");
        info.IsMacOS.Should().BeTrue();
        info.IsAndroid.Should().BeFalse();
    }

    [Theory]
    [InlineData(CrossTarget.AndroidArm64, "arm64-v8a")]
    [InlineData(CrossTarget.AndroidX64, "x86_64")]
    public void Android_targets_name_their_abi(CrossTarget target, string abi)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.AndroidAbi.Should().Be(abi);
        info.IsAndroid.Should().BeTrue();
        info.Notes.Should().Contain("Android Software Development Kit License Agreement");
    }

    [Fact]
    public void non_android_targets_have_no_abi()
        => CrossTargetInfo.All.Where(i => !i.IsAndroid).Should().OnlyContain(i => i.AndroidAbi == null);

    [Fact]
    public void Get_throws_for_an_undefined_value()
    {
        //Act
        var act = () => CrossTargetInfo.Get((CrossTarget)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryFromTriple_finds_a_target_case_insensitively()
    {
        //Act
        var found = CrossTargetInfo.TryFromTriple(" X86_64-PC-WINDOWS-GNULLVM ", out var info);

        //Assert
        found.Should().BeTrue();
        info.Target.Should().Be(CrossTarget.WindowsX64Gnullvm);
    }

    [Fact]
    public void TryFromTriple_returns_false_for_an_unknown_triple()
    {
        //Act
        var found = CrossTargetInfo.TryFromTriple("powerpc64le-unknown-linux-gnu", out var info);
        var riscv = CrossTargetInfo.TryFromTriple("riscv64gc-unknown-linux-gnu", out var riscvInfo);

        //Assert
        found.Should().BeFalse();
        info.Should().BeNull();
        riscv.Should().BeTrue();
        riscvInfo.Target.Should().Be(CrossTarget.LinuxRiscV64);
        CrossTargetInfo.TryFromTriple(string.Empty, out _).Should().BeFalse("LinuxNative has no triple of its own");
    }

    [Theory]
    [InlineData(CrossTarget.LinuxX64, "hello", "my-lib", "hello", "libmy_lib.so", "libmy_lib.a")]
    [InlineData(CrossTarget.WindowsX64Gnullvm, "hello", "my-lib", "hello.exe", "my_lib.dll", "libmy_lib.a")]
    [InlineData(CrossTarget.WindowsArm64Gnullvm, "hello", "my-lib", "hello.exe", "my_lib.dll", "libmy_lib.a")]
    [InlineData(CrossTarget.AndroidArm64, "hello", "my-lib", "hello", "libmy_lib.so", "libmy_lib.a")]
    public void file_names_follow_the_target_conventions(CrossTarget target, string bin, string lib, string exe, string shared, string staticLib)
    {
        //Act
        var info = CrossTargetInfo.Get(target);

        //Assert
        info.ExecutableFileName(bin).Should().Be(exe);
        info.SharedLibraryFileName(lib).Should().Be(shared);
        info.StaticLibraryFileName(lib).Should().Be(staticLib);
    }

    [Fact]
    public void ToString_shows_the_target_and_triple()
        => CrossTargetInfo.Get(CrossTarget.AndroidX64).ToString().Should().Be("AndroidX64 (x86_64-linux-android)");

    [Theory]
    [InlineData("aarch64", CrossTarget.AndroidArm64)]
    [InlineData("arm64", CrossTarget.AndroidX64)]
    [InlineData(null, CrossTarget.AndroidArm64)]
    public void EnsureEngineCanBuild_rejects_android_on_an_engine_that_is_not_x86_64(string architecture, CrossTarget target)
    {
        //Arrange
        var engine = new DockerEngineInfo { IsAvailable = true, IsLinuxDaemon = true, OsType = "linux", Architecture = architecture };

        //Act
        var act = () => CrossTargetInfo.EnsureEngineCanBuild(engine, new[] { CrossTarget.LinuxX64, target });

        //Assert
        act.Should().Throw<CrossTargetNotSupportedException>()
            .WithMessage("*needs an x86-64 Linux Docker engine*" + (architecture ?? "unknown") + "*")
            .Which.Target.Should().Be(target);
    }

    [Theory]
    [InlineData("x86_64")]
    [InlineData("amd64")]
    public void EnsureEngineCanBuild_accepts_android_on_an_x86_64_engine(string architecture)
    {
        //Arrange
        var engine = new DockerEngineInfo { IsAvailable = true, IsLinuxDaemon = true, OsType = "linux", Architecture = architecture };

        //Act
        var act = () => CrossTargetInfo.EnsureEngineCanBuild(engine, new[] { CrossTarget.AndroidArm64, CrossTarget.AndroidX64 });

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureEngineCanBuild_accepts_linux_and_windows_targets_on_an_arm64_engine()
    {
        //Arrange
        var engine = new DockerEngineInfo { IsAvailable = true, IsLinuxDaemon = true, OsType = "linux", Architecture = "aarch64" };

        //Act
        var act = () => CrossTargetInfo.EnsureEngineCanBuild(engine, new[] { CrossTarget.LinuxX64, CrossTarget.WindowsX64Gnullvm });

        //Assert
        act.Should().NotThrow();
    }
}
