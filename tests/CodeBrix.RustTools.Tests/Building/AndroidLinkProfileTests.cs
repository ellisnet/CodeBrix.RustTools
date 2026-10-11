using System;
using System.IO;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class AndroidLinkProfileTests
{
    private const string ContainerBin = "/opt/android-ndk/toolchains/llvm/prebuilt/linux-x86_64/bin";

    private const string ContainerSysroot = "/opt/android-ndk/toolchains/llvm/prebuilt/linux-x86_64/sysroot";

    [Fact]
    public void EnvironmentFor_sets_what_cargo_ndk_sets_for_the_arm64_target()
    {
        //Act
        var environment = new AndroidLinkProfile(ContainerBin).EnvironmentFor("aarch64-linux-android");

        //Assert
        environment["CC_aarch64_linux_android"].Should().Be(ContainerBin + "/aarch64-linux-android24-clang");
        environment["CXX_aarch64_linux_android"].Should().Be(ContainerBin + "/aarch64-linux-android24-clang++");
        environment["AR_aarch64_linux_android"].Should().Be(ContainerBin + "/llvm-ar");
        environment["RANLIB_aarch64_linux_android"].Should().Be(ContainerBin + "/llvm-ranlib");
        environment["CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER"].Should().Be(ContainerBin + "/aarch64-linux-android24-clang");
        environment["CARGO_TARGET_AARCH64_LINUX_ANDROID_AR"].Should().Be(ContainerBin + "/llvm-ar");
        environment["BINDGEN_EXTRA_CLANG_ARGS_aarch64_linux_android"].Should().Be("--target=aarch64-linux-android24 --sysroot=" + ContainerSysroot);
        environment.Count.Should().Be(7);
    }

    [Fact]
    public void EnvironmentFor_gives_bindgen_the_api_versioned_target_and_the_ndk_sysroot()
    {
        //Act
        var environment = new AndroidLinkProfile(ContainerBin, 33).EnvironmentFor("x86_64-linux-android");

        //Assert
        environment["BINDGEN_EXTRA_CLANG_ARGS_x86_64_linux_android"].Should().Be("--target=x86_64-linux-android33 --sysroot=" + ContainerSysroot);
        environment.Keys.Should().NotContain("BINDGEN_EXTRA_CLANG_ARGS");
    }

    [Fact]
    public void SysrootDirectory_is_the_sysroot_folder_next_to_bin()
        => new AndroidLinkProfile(ContainerBin + "/").SysrootDirectory.Should().Be(ContainerSysroot);

    [Fact]
    public void BindgenClangArguments_uses_forward_slashes_and_quotes_a_path_with_spaces()
    {
        //Act
        var arguments = new AndroidLinkProfile(@"C:\Users\Some One\Android\ndk\toolchains\llvm\prebuilt\windows-x86_64\bin", 24, windowsFileNames: true)
            .BindgenClangArguments("aarch64-linux-android");

        //Assert
        arguments.Should().Be("--target=aarch64-linux-android24 --sysroot=\"C:/Users/Some One/Android/ndk/toolchains/llvm/prebuilt/windows-x86_64/sysroot\"");
    }

    [Fact]
    public void BindgenClangArguments_rejects_a_triple_that_is_not_android()
    {
        //Act
        var act = () => new AndroidLinkProfile(ContainerBin).BindgenClangArguments("x86_64-unknown-linux-gnu");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnvironmentFor_honours_the_api_level_and_the_x64_target()
        => new AndroidLinkProfile(ContainerBin + "/", 30).EnvironmentFor("X86_64-LINUX-ANDROID")["CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER"]
            .Should().Be(ContainerBin + "/x86_64-linux-android30-clang");

    [Fact]
    public void EnvironmentFor_uses_windows_file_names_and_keeps_a_windows_path_s_separators()
    {
        //Act
        var environment = new AndroidLinkProfile(@"C:\Android\ndk\bin", 24, windowsFileNames: true).EnvironmentFor("aarch64-linux-android");

        //Assert
        environment["CC_aarch64_linux_android"].Should().Be(@"C:\Android\ndk\bin\aarch64-linux-android24-clang.cmd");
        environment["CARGO_TARGET_AARCH64_LINUX_ANDROID_AR"].Should().Be(@"C:\Android\ndk\bin\llvm-ar.exe");
        environment["RANLIB_aarch64_linux_android"].Should().Be(@"C:\Android\ndk\bin\llvm-ranlib.exe");
    }

    [Fact]
    public void EnvironmentFor_rejects_a_non_android_triple()
    {
        //Act
        var act = () => new AndroidLinkProfile(ContainerBin).EnvironmentFor("aarch64-apple-darwin");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*aarch64-apple-darwin*");
    }

    [Fact]
    public void ApplyTo_sets_the_target_and_the_environment()
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.Environment["RUST_LOG"] = "debug";

        //Act
        new AndroidLinkProfile(ContainerBin).ApplyTo(options, "x86_64-linux-android");

        //Assert
        options.TargetTriple.Should().Be("x86_64-linux-android");
        options.Environment["CC_x86_64_linux_android"].Should().Be(ContainerBin + "/x86_64-linux-android24-clang");
        options.Environment["RUST_LOG"].Should().Be("debug");
    }

    [Theory]
    [InlineData("aarch64-linux-android", "arm64-v8a", "android-arm64")]
    [InlineData("x86_64-linux-android", "x86_64", "android-x64")]
    public void AbiOf_and_RuntimeIdentifierOf_name_the_targets(string triple, string abi, string rid)
    {
        AndroidLinkProfile.AbiOf(triple).Should().Be(abi);
        AndroidLinkProfile.RuntimeIdentifierOf(triple).Should().Be(rid);
        AndroidLinkProfile.IsAndroidTriple(triple).Should().BeTrue();
    }

    [Fact]
    public void Constructor_rejects_a_blank_folder_and_a_pre_64_bit_api_level()
    {
        //Act
        var blank = () => new AndroidLinkProfile(" ");
        var old = () => new AndroidLinkProfile(ContainerBin, 19);

        //Assert
        blank.Should().Throw<ArgumentException>();
        old.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ForNdk_uses_the_ndk_s_toolchain_and_rejects_an_api_level_it_lacks()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeAndroidNdk.Create(Path.Combine(sandbox.Directory, "ndk"), "30.0.16248370", "linux-x86_64", new[] { 21, 24 });
        var ndk = AndroidNdk.Load(root, "linux-x86_64");

        //Act
        var profile = AndroidLinkProfile.ForNdk(ndk);
        var unsupported = () => AndroidLinkProfile.ForNdk(ndk, 33);

        //Assert
        profile.ToolchainBinDirectory.Should().Be(ndk.ToolchainBinDirectory);
        profile.ApiLevel.Should().Be(24);
        profile.ClangPath("aarch64-linux-android").Should().Be(ndk.ClangWrapperPath("aarch64-linux-android", 24));
        unsupported.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*21 to 24*");
    }
}
