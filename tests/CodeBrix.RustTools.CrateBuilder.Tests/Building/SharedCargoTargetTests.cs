using System.IO;
using System.Linq;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class SharedCargoTargetTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_cargo_target_directory_nothing_is_shared(string value)
    {
        //Arrange
        var options = new CrateBuilderOptions { CargoTargetDirectory = value };
        var cargo = new CargoBuildOptions();
        var container = new BuildContainerOptions();

        //Act
        var host = SharedCargoTarget.For(options, SharedCargoTarget.HostFolderName);
        SharedCargoTarget.ApplyHost(cargo, host);
        SharedCargoTarget.ApplyContainer(cargo, SharedCargoTarget.For(options, SharedCargoTarget.DockerFolderName));
        SharedCargoTarget.Mount(container, SharedCargoTarget.For(options, SharedCargoTarget.DockerFolderName));

        //Assert
        SharedCargoTarget.Root(options).Should().BeNull();
        host.Should().BeNull();
        cargo.Environment.Should().BeEmpty("the default keeps each run's own target folder under the scratch root");
        container.Mounts.Should().BeEmpty();
        ApiExtractor.SharedToolTargetDirectory(options, SharedCargoTarget.ExtractorHostFolderName).Should().BeNull();
    }

    [Fact]
    public void The_default_options_have_no_cargo_target_directory() =>
        new CrateBuilderOptions().CargoTargetDirectory.Should().BeNull();

    [Fact]
    public void Each_kind_of_build_gets_its_own_sub_folder_of_the_full_path()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var options = new CrateBuilderOptions { CargoTargetDirectory = Path.Combine(sandbox.Directory, "shared") + Path.DirectorySeparatorChar };
        var root = Path.Combine(sandbox.Directory, "shared");
        var hash = ApiExtractorToolSource.ContentHash.Substring(0, ApiExtractorToolSource.FolderHashLength);

        //Act + Assert
        SharedCargoTarget.Root(options).Should().Be(root);
        SharedCargoTarget.For(options, SharedCargoTarget.HostFolderName).Should().Be(Path.Combine(root, "host"));
        SharedCargoTarget.For(options, SharedCargoTarget.DockerFolderName).Should().Be(Path.Combine(root, "docker"));
        ApiExtractor.SharedToolTargetDirectory(options, SharedCargoTarget.ExtractorHostFolderName).Should().Be(Path.Combine(root, "api-extractor", hash));
        ApiExtractor.SharedToolTargetDirectory(options, SharedCargoTarget.ExtractorDockerFolderName).Should().Be(Path.Combine(root, "api-extractor-docker", hash));
    }

    [Fact]
    public void A_relative_cargo_target_directory_is_resolved_against_the_current_folder() =>
        SharedCargoTarget.Root(new CrateBuilderOptions { CargoTargetDirectory = "shared-target" }).Should().Be(Path.GetFullPath("shared-target"));

    [Fact]
    public void A_host_run_gets_CARGO_TARGET_DIR_and_leaves_creating_the_folder_to_cargo()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var options = new CargoBuildOptions();
        options.Environment["CARGO_TARGET_DIR"] = "/from/the/consumer";
        var folder = Path.Combine(sandbox.Directory, "shared", "host");

        //Act
        SharedCargoTarget.ApplyHost(options, folder);

        //Assert
        options.Environment["CARGO_TARGET_DIR"].Should().Be(folder, "the option replaces a CARGO_TARGET_DIR from elsewhere");
        Directory.Exists(folder).Should().BeFalse("cargo creates it, with its CACHEDIR.TAG");
    }

    [Fact]
    public void A_container_run_points_cargo_at_the_mount_and_the_container_mounts_the_host_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = Path.Combine(sandbox.Directory, "shared", "docker");
        var cargo = new CargoBuildOptions();

        //Act
        SharedCargoTarget.ApplyContainer(cargo, folder);
        var container = DockerBuildDriver.ContainerOptionsFor(new CrateBuildRequest(), folder);

        //Assert
        cargo.Environment["CARGO_TARGET_DIR"].Should().Be("/codebrix-cargo-target");
        container.Mounts.Should().HaveCount(1);
        var mount = container.Mounts.Single();
        mount.Kind.Should().Be(MountKind.Bind);
        mount.Source.Should().Be(folder);
        mount.Target.Should().Be(SharedCargoTarget.ContainerPath);
        mount.ReadOnly.Should().BeFalse();
        container.AndroidApiLevel.Should().Be(24);
        Directory.Exists(folder).Should().BeTrue();
        DockerBuildDriver.ContainerOptionsFor(new CrateBuildRequest()).Mounts.Should().BeEmpty();
    }

    [Fact]
    public void The_container_is_mounted_outside_work_and_ownership_goes_back_to_the_folders_owner()
    {
        //Assert
        SharedCargoTarget.ContainerPath.StartsWith(BuildContainer.WorkDirectory + "/", System.StringComparison.Ordinal).Should().BeFalse();
        SharedCargoTarget.OwnershipCommand().Should().Equal("sh", "-c", "chown -R \"$(stat -c %u:%g /codebrix-cargo-target)\" /codebrix-cargo-target");
    }

    [Fact]
    public void ManyLinuxFolderFor_gives_each_manylinux_image_its_own_folder_below_docker()
    {
        //Arrange
        var options = new CrateBuilderOptions { CargoTargetDirectory = "/data/target" };
        var docker = Path.Combine(Path.GetFullPath("/data/target"), "docker"); // \data\target on the current drive on Windows

        //Assert
        SharedCargoTarget.ManyLinuxFolderFor(options, CrateTarget.LinuxX64).Should().Be(Path.Combine(docker, "manylinux-x86_64"));
        SharedCargoTarget.ManyLinuxFolderFor(options, CrateTarget.LinuxArm64).Should().Be(Path.Combine(docker, "manylinux-aarch64"));
        SharedCargoTarget.ManyLinuxFolderFor(options, CrateTarget.LinuxRiscV64).Should().Be(Path.Combine(docker, "manylinux-riscv64"));
        SharedCargoTarget.ManyLinuxFolderFor(new CrateBuilderOptions(), CrateTarget.LinuxArm64).Should().BeNull();
    }
}
