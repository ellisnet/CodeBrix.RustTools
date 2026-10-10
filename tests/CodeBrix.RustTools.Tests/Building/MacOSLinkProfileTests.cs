using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class MacOSLinkProfileTests
{
    private static MacOSLinkProfile Profile(string registry = "/home/me/.cargo/registry/src")
        => new MacOSLinkProfile("/toolchain/lib/rustlib/x86_64-unknown-linux-gnu/bin/rust-lld", "/opt/sysroot", registry);

    [Fact]
    public void Triples_are_the_two_macos_targets()
        => MacOSLinkProfile.Triples.Should().Equal("aarch64-apple-darwin", "x86_64-apple-darwin");

    [Theory]
    [InlineData("aarch64-apple-darwin", true)]
    [InlineData("X86_64-APPLE-DARWIN", true)]
    [InlineData("x86_64-unknown-linux-gnu", false)]
    [InlineData(null, false)]
    public void IsMacOSTriple_recognises_the_darwin_triples(string triple, bool expected)
        => MacOSLinkProfile.IsMacOSTriple(triple).Should().Be(expected);

    [Fact]
    public void EnvironmentTripleName_uses_cargo_s_upper_snake_case()
        => MacOSLinkProfile.EnvironmentTripleName("aarch64-apple-darwin").Should().Be("AARCH64_APPLE_DARWIN");

    [Fact]
    public void RustFlags_use_the_ld64_flavour_strip_the_debug_map_and_remap_the_registry()
        => Profile().RustFlagsText().Should().Be("-C linker-flavor=ld64.lld -C link-arg=-S --remap-path-prefix=/home/me/.cargo/registry/src=/cargo-registry-src");

    [Fact]
    public void RustFlags_omit_the_remapping_without_a_registry_folder()
        => Profile(null).RustFlagsText().Should().Be("-C linker-flavor=ld64.lld -C link-arg=-S");

    [Fact]
    public void RustFlags_append_the_install_name_as_two_linker_arguments()
        => Profile(null).RustFlags("@rpath/libx.dylib").Should().Equal("-C", "linker-flavor=ld64.lld", "-C", "link-arg=-S", "-C", "link-arg=-install_name", "-C", "link-arg=@rpath/libx.dylib");

    [Fact]
    public void EnvironmentFor_sets_the_linker_rustflags_sdkroot_and_deployment_target()
    {
        //Act
        var environment = Profile().EnvironmentFor("aarch64-apple-darwin");

        //Assert
        environment["CARGO_TARGET_AARCH64_APPLE_DARWIN_LINKER"].Should().Be("/toolchain/lib/rustlib/x86_64-unknown-linux-gnu/bin/rust-lld");
        environment["CARGO_TARGET_AARCH64_APPLE_DARWIN_RUSTFLAGS"].Should().Be(Profile().RustFlagsText());
        environment["SDKROOT"].Should().Be("/opt/sysroot");
        environment["MACOSX_DEPLOYMENT_TARGET"].Should().Be("11.0");
        environment.Count.Should().Be(4);
    }

    [Fact]
    public void EnvironmentFor_rejects_a_non_macos_triple()
    {
        //Act
        var act = () => Profile().EnvironmentFor("x86_64-pc-windows-gnu");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*x86_64-pc-windows-gnu*");
    }

    [Fact]
    public void ApplyTo_sets_the_target_the_install_name_and_the_environment()
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.Environment["RUST_LOG"] = "debug";

        //Act
        Profile().ApplyTo(options, "x86_64-apple-darwin", "@rpath/libx.dylib");

        //Assert
        options.TargetTriple.Should().Be("x86_64-apple-darwin");
        options.MacOSInstallName.Should().Be("@rpath/libx.dylib");
        options.Environment["CARGO_TARGET_X86_64_APPLE_DARWIN_RUSTFLAGS"].Should().EndWith("-C link-arg=-install_name -C link-arg=@rpath/libx.dylib");
        options.Environment["RUST_LOG"].Should().Be("debug");
        options.Environment["SDKROOT"].Should().Be("/opt/sysroot");
    }

    [Fact]
    public void ApplyTo_uses_the_install_name_already_in_the_options()
    {
        //Arrange
        var options = new CargoBuildOptions { MacOSInstallName = "@rpath/liby.dylib" };

        //Act
        Profile().ApplyTo(options, "aarch64-apple-darwin");

        //Assert
        options.Environment["CARGO_TARGET_AARCH64_APPLE_DARWIN_RUSTFLAGS"].Should().EndWith("link-arg=@rpath/liby.dylib");
    }

    [Fact]
    public void Constructor_requires_the_linker_and_the_sysroot()
    {
        //Act
        var noLinker = () => new MacOSLinkProfile(" ", "/opt/sysroot");
        var noSysroot = () => new MacOSLinkProfile("/lld", null);

        //Assert
        noLinker.Should().Throw<ArgumentException>();
        noSysroot.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultInstallName_names_the_single_cdylib_of_sample_13()
        => MacOSLinkProfile.DefaultInstallName(CargoProject.Open(RepoPaths.Sample(13))).Should().Be("@rpath/libtest_project_13.dylib");

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void DefaultInstallName_is_null_without_exactly_one_cdylib(int sample)
        => MacOSLinkProfile.DefaultInstallName(CargoProject.Open(RepoPaths.Sample(sample))).Should().BeNull();

    [Fact]
    public void ForToolchain_needs_a_detected_sysroot()
    {
        //Act
        var act = () => MacOSLinkProfile.ForToolchain(TestToolchains.Complete(), "/opt/sysroot");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*sysroot*");
    }

    [Fact]
    public async Task ForToolchain_finds_rust_lld_in_this_machine_s_toolchain_and_needs_a_stub()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var toolchain = await new RustToolchainDetector().DetectAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var sysroot = SampleSandbox.CreateEmpty();

        //Act
        var noStub = () => MacOSLinkProfile.ForToolchain(toolchain, sysroot.Directory);
        noStub.Should().Throw<FileNotFoundException>();
        Directory.CreateDirectory(Path.Combine(sysroot.Directory, "usr", "lib"));
        File.WriteAllText(Path.Combine(sysroot.Directory, "usr", "lib", "libSystem.tbd"), "--- !tapi-tbd\n");
        var profile = MacOSLinkProfile.ForToolchain(toolchain, sysroot.Directory);

        //Assert
        File.Exists(profile.RustLldPath).Should().BeTrue(profile.RustLldPath);
        profile.RustLldPath.Should().Contain(Path.Combine("lib", "rustlib", toolchain.HostTriple, "bin"));
        profile.CargoRegistrySourceDirectory.Should().Be(Path.Combine(toolchain.CargoHome, "registry", "src"));
        profile.SysrootDirectory.Should().Be(sysroot.Directory);
    }
}
