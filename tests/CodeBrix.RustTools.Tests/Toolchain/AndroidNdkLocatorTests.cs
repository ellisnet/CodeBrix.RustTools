using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class AndroidNdkLocatorTests
{
    private const string Linux = "linux-x86_64";

    private static Func<string, string> Environment(params (string Name, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    [Theory]
    [InlineData(OperatingSystemKind.MacOS, Architecture.Arm64, "darwin-x86_64")]
    [InlineData(OperatingSystemKind.MacOS, Architecture.X64, "darwin-x86_64")]
    [InlineData(OperatingSystemKind.Windows, Architecture.X64, "windows-x86_64")]
    [InlineData(OperatingSystemKind.Windows, Architecture.Arm64, null)]
    [InlineData(OperatingSystemKind.DebianLinux, Architecture.X64, "linux-x86_64")]
    [InlineData(OperatingSystemKind.OtherLinux, Architecture.X64, "linux-x86_64")]
    [InlineData(OperatingSystemKind.OtherLinux, Architecture.Arm64, null)]
    [InlineData(OperatingSystemKind.Unknown, Architecture.X64, null)]
    public void HostTagFor_follows_google_s_host_support(OperatingSystemKind os, Architecture architecture, string expected)
        => AndroidNdkLocator.HostTagFor(os, architecture).Should().Be(expected);

    [Fact]
    public void HostSupportMessage_explains_an_unsupported_machine_and_is_null_otherwise()
        => (AndroidNdkLocator.HostTag == null).Should().Be(AndroidNdkLocator.HostSupportMessage != null);

    [Fact]
    public void TryLoad_reads_the_version_the_api_levels_and_the_tool_paths()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeAndroidNdk.Create(Path.Combine(sandbox.Directory, "ndk"), "30.0.16248370", Linux, new[] { 21, 24, 35 });

        //Act
        var loaded = AndroidNdk.TryLoad(root, Linux, out var ndk);

        //Assert
        loaded.Should().BeTrue();
        ndk.Version.Should().Be("30.0.16248370");
        ndk.HostTag.Should().Be(Linux);
        ndk.ApiLevels.Should().Equal(21, 24, 35);
        ndk.SupportsApiLevel(24).Should().BeTrue();
        ndk.SupportsApiLevel(30).Should().BeFalse();
        ndk.WindowsFileNames.Should().BeFalse();
        ndk.ToolchainBinDirectory.Should().Be(Path.Combine(root, "toolchains", "llvm", "prebuilt", Linux, "bin"));
        ndk.ArchiverPath.Should().Be(Path.Combine(ndk.ToolchainBinDirectory, "llvm-ar"));
        ndk.ClangWrapperPath("aarch64-linux-android", 24).Should().Be(Path.Combine(ndk.ToolchainBinDirectory, "aarch64-linux-android24-clang"));
        ndk.ClangPlusPlusWrapperPath("x86_64-linux-android", 21).Should().EndWith("x86_64-linux-android21-clang++");
        ndk.ToString().Should().Contain("30.0.16248370").And.Contain(Linux);
    }

    [Fact]
    public void TryLoad_uses_windows_file_names_for_a_windows_host_tag()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeAndroidNdk.Create(Path.Combine(sandbox.Directory, "ndk"), "29.0.14033849", "windows-x86_64", new[] { 24 });

        //Act
        AndroidNdk.TryLoad(root, "windows-x86_64", out var ndk).Should().BeTrue();

        //Assert
        ndk.WindowsFileNames.Should().BeTrue();
        ndk.ApiLevels.Should().Equal(24);
        ndk.ArchiverPath.Should().EndWith("llvm-ar.exe");
        ndk.ClangWrapperPath("aarch64-linux-android", 24).Should().EndWith("aarch64-linux-android24-clang.cmd");
    }

    [Fact]
    public void TryLoad_rejects_a_folder_without_a_toolchain_for_the_host_tag_and_tolerates_a_missing_version()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeAndroidNdk.Create(Path.Combine(sandbox.Directory, "ndk"), null, Linux, new[] { 24 });

        //Act
        var wrongHost = AndroidNdk.TryLoad(root, "windows-x86_64", out var none);
        var ok = AndroidNdk.TryLoad(root, Linux, out var ndk);

        //Assert
        wrongHost.Should().BeFalse();
        none.Should().BeNull();
        ok.Should().BeTrue();
        ndk.Version.Should().BeNull();
        AndroidNdk.TryLoad(sandbox.Directory, Linux, out _).Should().BeFalse();
    }

    [Fact]
    public void Load_throws_for_a_folder_that_is_not_an_ndk()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var act = () => AndroidNdk.Load(sandbox.Directory, Linux);

        //Assert
        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*toolchains/llvm/prebuilt/linux-x86_64/bin*");
    }

    [Fact]
    public void FindInSdk_picks_the_newest_side_by_side_ndk_by_numeric_version()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var sdk = FakeAndroidNdk.CreateSdk(Path.Combine(sandbox.Directory, "sdk"), Linux, "9.9.9", "30.0.16248370", "29.0.14033849");

        //Act
        var ndk = AndroidNdkLocator.FindInSdk(sdk, Linux);

        //Assert
        ndk.Version.Should().Be("30.0.16248370");
        AndroidNdkLocator.ListSideBySideNdks(sdk).Select(Path.GetFileName).Should().Equal("30.0.16248370", "29.0.14033849", "9.9.9");
    }

    [Fact]
    public void FindInSdk_falls_back_to_the_legacy_ndk_bundle_and_returns_null_without_any()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var sdk = Path.Combine(sandbox.Directory, "sdk");
        Directory.CreateDirectory(sdk);
        AndroidNdkLocator.FindInSdk(sdk, Linux).Should().BeNull();
        FakeAndroidNdk.Create(Path.Combine(sdk, "ndk-bundle"), "21.4.7075529", Linux, new[] { 21 });

        //Act
        var ndk = AndroidNdkLocator.FindInSdk(sdk, Linux);

        //Assert
        ndk.Version.Should().Be("21.4.7075529");
        ndk.Path.Should().Be(Path.Combine(sdk, "ndk-bundle"));
    }

    [Fact]
    public void Find_prefers_the_ndk_variables_then_the_sdk_variables()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var direct = FakeAndroidNdk.Create(Path.Combine(sandbox.Directory, "direct"), "28.2.13676358", Linux, new[] { 24 });
        var sdk = FakeAndroidNdk.CreateSdk(Path.Combine(sandbox.Directory, "sdk"), Linux, "30.0.16248370");

        //Act
        var viaNdkHome = AndroidNdkLocator.Find(null, Environment(("ANDROID_NDK_HOME", direct), ("ANDROID_HOME", sdk)), Linux);
        var viaNdkRoot = AndroidNdkLocator.Find(null, Environment(("ANDROID_NDK_ROOT", direct)), Linux);
        var viaSdkHome = AndroidNdkLocator.Find(null, Environment(("ANDROID_HOME", sdk)), Linux);
        var viaSdkRoot = AndroidNdkLocator.Find(null, Environment(("ANDROID_SDK_ROOT", sdk)), Linux);
        var nothing = AndroidNdkLocator.Find(null, Environment(("ANDROID_NDK_HOME", sandbox.Directory)), Linux);

        //Assert
        viaNdkHome.Version.Should().Be("28.2.13676358");
        viaNdkRoot.Version.Should().Be("28.2.13676358");
        viaSdkHome.Version.Should().Be("30.0.16248370");
        viaSdkRoot.Version.Should().Be("30.0.16248370");
        nothing.Should().BeNull();
    }

    [Fact]
    public void Find_uses_an_explicit_path_without_falling_back_and_returns_null_without_a_host_tag()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var sdk = FakeAndroidNdk.CreateSdk(Path.Combine(sandbox.Directory, "sdk"), Linux, "30.0.16248370");
        var environment = Environment(("ANDROID_HOME", sdk));

        //Act
        var explicitNdk = AndroidNdkLocator.Find(Path.Combine(sdk, "ndk", "30.0.16248370"), environment, Linux);
        var wrongExplicit = AndroidNdkLocator.Find(sandbox.Directory, environment, Linux);
        var noHost = AndroidNdkLocator.Find(null, environment, null);

        //Assert
        explicitNdk.Version.Should().Be("30.0.16248370");
        wrongExplicit.Should().BeNull();
        noHost.Should().BeNull();
    }

    [Fact]
    public void Find_on_this_machine_returns_null_or_a_usable_ndk()
    {
        //Act
        var ndk = AndroidNdkLocator.Find();

        //Assert
        if (ndk != null)
        {
            Directory.Exists(ndk.ToolchainBinDirectory).Should().BeTrue(ndk.ToString());
            ndk.ApiLevels.Should().NotBeEmpty();
        }
    }
}
