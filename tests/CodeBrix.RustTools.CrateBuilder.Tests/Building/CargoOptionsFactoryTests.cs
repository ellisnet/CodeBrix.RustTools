using System;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class CargoOptionsFactoryTests
{
    [Fact]
    public void Create_carries_the_request_settings_and_selects_the_package()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(13);
        var crate = BuiltCrate.From(new StagedWorkspace { Directory = sandbox.Directory, ManifestPath = System.IO.Path.Combine(sandbox.Directory, "Cargo.toml") });
        var request = new CrateBuildRequest
        {
            Profile = CrateBuildProfile.Debug,
            NoDefaultFeatures = true,
            Offline = true,
            Locked = true,
            LicensePolicy = LicensePolicy.Permissive(),
            Timeout = TimeSpan.FromMinutes(3),
        };
        request.Features.Add(" fast ");

        //Act
        var options = CargoOptionsFactory.Create(request, crate);

        //Assert
        options.Profile.Should().Be(CargoProfile.Debug);
        options.NoDefaultFeatures.Should().BeTrue();
        options.Offline.Should().BeTrue();
        options.Locked.Should().BeTrue();
        options.Features.Should().Equal("fast");
        options.Packages.Should().Equal("test_project_13");
        options.LicensePolicy.Should().NotBeNull();
        options.LicenseGateMode.Should().Be(LicenseGateMode.Enforce);
        options.ThrowOnLicenseViolation.Should().BeFalse();
        options.Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Create_for_a_facade_never_passes_locked_or_the_features_but_keeps_offline()
    {
        //Arrange
        var request = new CrateBuildRequest { NoDefaultFeatures = true, AllFeatures = true, Offline = true, Locked = true };
        request.Features.Add("fast");

        //Act
        var options = CargoOptionsFactory.Create(request, BuiltCrateOf13(), isFacade: true);

        //Assert
        options.Locked.Should().BeFalse();
        options.Offline.Should().BeTrue();
        options.Features.Should().BeEmpty();
        options.NoDefaultFeatures.Should().BeFalse();
        options.AllFeatures.Should().BeFalse();
        options.Packages.Should().Equal("test_project_13");
    }

    [Fact]
    public void A_facade_build_reads_the_upstream_workspaces_cargo_config_through_a_relative_path()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var stage = System.IO.Path.Combine(sandbox.Directory, "stage", "shape");
        sandbox.WriteFile("stage/shape/src/Cargo.toml", "[workspace]\n");
        sandbox.WriteFile("stage/shape/src/.cargo/config.toml", "[env]\nSHAPE = \"1\"\n");
        var facade = sandbox.WriteFile("stage/shape/.codebrix-facade/shape_facade/Cargo.toml", "[package]\n");
        var workspace = new StagedWorkspace { Directory = stage, WorkspaceManifestPath = System.IO.Path.Combine(stage, "src", "Cargo.toml"), ManifestPath = facade, IsFacade = true };
        var options = new CargoBuildOptions();

        //Act
        CargoOptionsFactory.AddUpstreamConfig(options, workspace);

        //Assert
        options.ExtraArguments.Should().Equal("--config", "../../src/.cargo/config.toml");
        workspace.IsFacade = false;
        CargoOptionsFactory.UpstreamConfigArgument(workspace).Should().BeNull("cargo finds the configuration itself when it builds the crate in its own tree");
    }

    [Fact]
    public void A_facade_build_without_an_upstream_cargo_config_adds_nothing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var stage = System.IO.Path.Combine(sandbox.Directory, "stage", "shape");
        sandbox.WriteFile("stage/shape/src/Cargo.toml", "[workspace]\n");
        var facade = sandbox.WriteFile("stage/shape/.codebrix-facade/shape_facade/Cargo.toml", "[package]\n");
        var options = new CargoBuildOptions();

        //Act
        CargoOptionsFactory.AddUpstreamConfig(options, new StagedWorkspace { Directory = stage, WorkspaceManifestPath = System.IO.Path.Combine(stage, "src", "Cargo.toml"), ManifestPath = facade, IsFacade = true });

        //Assert
        options.ExtraArguments.Should().BeEmpty();
    }

    [Fact]
    public void Release_is_the_default_profile() =>
        CargoOptionsFactory.Create(new CrateBuildRequest(), BuiltCrateOf13()).Profile.Should().Be(CargoProfile.Release);

    [Fact]
    public void RustFlagsVariable_is_cargos_per_target_variable() =>
        CargoOptionsFactory.RustFlagsVariable("riscv64gc-unknown-linux-gnu").Should().Be("CARGO_TARGET_RISCV64GC_UNKNOWN_LINUX_GNU_RUSTFLAGS");

    [Fact]
    public void MacOSInstallName_uses_the_library_name() =>
        CargoOptionsFactory.MacOSInstallName(BuiltCrateOf13()).Should().Be("@rpath/libtest_project_13.dylib");

    [Fact]
    public void BuiltCrate_reads_the_package_and_library_names()
    {
        //Act
        var crate = BuiltCrateOf13();

        //Assert
        crate.PackageName.Should().Be("test_project_13");
        crate.LibraryName.Should().Be("test_project_13");
        crate.ManifestPath.Should().EndWith("Cargo.toml");
    }

    [Fact]
    public void BuiltCrate_refuses_a_missing_manifest() =>
        ((Action)(() => BuiltCrate.From(new StagedWorkspace { ManifestPath = "/nonexistent/Cargo.toml" }))).Should().Throw<CrateBuilderException>();

    private static BuiltCrate BuiltCrateOf13()
        => BuiltCrate.From(new StagedWorkspace { ManifestPath = System.IO.Path.Combine(RepoPaths.Sample(13), "Cargo.toml") });
}
