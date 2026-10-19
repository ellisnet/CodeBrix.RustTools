using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class TargetMatrixTests
{
    private static CrateBuildRequest Request(BuildMode mode, LinuxBaseline baseline, params CrateTarget[] targets) => new CrateBuildRequest
    {
        Source = new LocalManifestSource("/src/crate/Cargo.toml"),
        Mode = mode,
        LinuxBaseline = baseline,
        Targets = targets.ToList(),
        OutputDirectory = "/out",
    };

    private static CrateReadinessReport Readiness(BuildMode mode, IReadOnlyList<CrateTarget> buildable, IReadOnlyList<CrateTarget> unbuildable, DockerEngineInfo engine = null, params BuildReadinessIssue[] issues) => new CrateReadinessReport
    {
        IsReady = buildable.Count > 0,
        Mode = mode,
        BuildableTargets = buildable,
        UnbuildableTargets = unbuildable,
        Issues = issues,
        Engine = engine,
    };

    private static DockerEngineInfo Engine(string architecture) => new DockerEngineInfo
    {
        IsAvailable = true,
        Architecture = architecture,
        OsType = "linux",
        IsLinuxDaemon = true,
        DockerCliFound = true,
        HostOperatingSystem = OperatingSystemKind.DebianLinux,
    };

    [Fact]
    public void readiness_unbuildable_targets_become_not_buildable_results_and_the_others_build()
    {
        //Arrange
        var request = Request(BuildMode.Host, LinuxBaseline.RustImage, CrateTarget.LinuxX64, CrateTarget.OsxArm64, CrateTarget.WinX64);
        var issue = new BuildReadinessIssue { Code = "target.target-missing", Message = "aarch64-apple-darwin is not installed.", Target = CrateTarget.OsxArm64 };
        var readiness = Readiness(BuildMode.Host, new[] { CrateTarget.LinuxX64, CrateTarget.WinX64 }, new[] { CrateTarget.OsxArm64 }, null, issue);

        //Act
        var matrix = TargetMatrix.Create(request, readiness);

        //Assert
        matrix.Mode.Should().Be(BuildMode.Host);
        matrix.Buildable.Should().Equal(CrateTarget.LinuxX64, CrateTarget.WinX64);
        matrix.NotBuildable.Should().HaveCount(1);
        var result = matrix.NotBuildable[0];
        result.Target.Should().Be(CrateTarget.OsxArm64);
        result.Rid.Should().Be("osx-arm64");
        result.Triple.Should().Be("aarch64-apple-darwin");
        result.Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        result.Message.Should().Contain("aarch64-apple-darwin is not installed");
        result.Provenance.Should().Be(ArtifactProvenance.DevelopmentBuild);
        matrix.IsBuildable(CrateTarget.WinX64).Should().BeTrue();
        matrix.IsBuildable(CrateTarget.OsxArm64).Should().BeFalse();
    }

    [Fact]
    public void a_readiness_report_that_is_not_ready_rules_out_every_target()
    {
        //Arrange
        var request = Request(BuildMode.Docker, LinuxBaseline.RustImage, CrateTarget.LinuxX64, CrateTarget.WinX64);
        var blocking = new BuildReadinessIssue { Code = "docker.engine-unavailable", Message = "The Docker engine cannot be used.", IsBlocking = true };
        var readiness = Readiness(BuildMode.Docker, new CrateTarget[0], request.Targets.ToList(), null, blocking);

        //Act
        var matrix = TargetMatrix.Create(request, readiness);

        //Assert
        matrix.Buildable.Should().BeEmpty();
        matrix.NotBuildable.Select(r => r.Target).Should().Equal(CrateTarget.LinuxX64, CrateTarget.WinX64);
        matrix.NotBuildable.Should().OnlyContain(r => r.Message.Contains("cannot be used"));
    }

    [Fact]
    public void without_a_readiness_report_only_the_driver_limits_apply()
    {
        //Arrange
        var request = Request(BuildMode.Host, LinuxBaseline.RustImage, CrateTarget.LinuxX64, CrateTarget.AndroidArm64);

        //Act
        var matrix = TargetMatrix.Create(request, null);

        //Assert
        matrix.Buildable.Should().Equal(CrateTarget.LinuxX64, CrateTarget.AndroidArm64);
        matrix.NotBuildable.Should().BeEmpty();
    }

    [Fact]
    public void manylinux_builds_the_three_linux_targets_and_names_the_reason_for_the_others()
    {
        //Arrange
        var request = Request(BuildMode.Docker, LinuxBaseline.ManyLinux, CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64, CrateTarget.WinX64, CrateTarget.AndroidArm64);
        var readiness = Readiness(BuildMode.Docker, request.Targets.ToList(), new CrateTarget[0], Engine("x86_64"));

        //Act
        var matrix = TargetMatrix.Create(request, readiness);

        //Assert
        matrix.Buildable.Should().Equal(CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64);
        matrix.NotBuildable.Select(r => r.Target).Should().Equal(CrateTarget.WinX64, CrateTarget.AndroidArm64);
        matrix.NotBuildable.Should().OnlyContain(r => r.Message == ManyLinuxImages.NonLinuxReason);
    }

    [Fact]
    public void manylinux_leaves_a_foreign_architecture_to_the_driver_s_platform_probe()
    {
        //Arrange (on an arm64 engine linux-x64 is the foreign one)
        var request = Request(BuildMode.Docker, LinuxBaseline.ManyLinux, CrateTarget.LinuxX64, CrateTarget.LinuxArm64);
        var readiness = Readiness(BuildMode.Docker, request.Targets.ToList(), new CrateTarget[0], Engine("aarch64"));

        //Act
        var matrix = TargetMatrix.Create(request, readiness);

        //Assert
        matrix.Buildable.Should().Equal(CrateTarget.LinuxX64, CrateTarget.LinuxArm64);
        matrix.NotBuildable.Should().BeEmpty();
        TargetMatrix.ManyLinuxLimit(LinuxBaseline.ManyLinux, CrateTarget.LinuxRiscV64, Engine("x86_64")).Should().BeNull();
        TargetMatrix.ManyLinuxLimit(LinuxBaseline.ManyLinux, CrateTarget.OsxArm64, Engine("x86_64")).Should().Be(ManyLinuxImages.NonLinuxReason);
        TargetMatrix.ManyLinuxLimit(LinuxBaseline.RustImage, CrateTarget.OsxArm64, Engine("x86_64")).Should().BeNull();
    }

    [Fact]
    public void the_rust_image_baseline_has_no_driver_limits_in_docker_mode()
    {
        //Arrange
        var request = Request(BuildMode.Docker, LinuxBaseline.RustImage, CrateTarget.LinuxArm64, CrateTarget.OsxX64);

        //Act
        var limits = request.Targets.Select(t => TargetMatrix.DriverLimit(request, t, Engine("x86_64"))).ToList();

        //Assert
        limits.Should().OnlyContain(l => l == null);
    }

    [Fact]
    public void ForDocker_maps_an_engine_check_through_the_readiness_rules()
    {
        //Arrange
        var request = Request(BuildMode.Docker, LinuxBaseline.RustImage, CrateTarget.LinuxX64, CrateTarget.AndroidArm64);

        //Act
        var matrix = TargetMatrix.ForDocker(request, Engine("aarch64"));

        //Assert
        matrix.Buildable.Should().Equal(CrateTarget.LinuxX64);
        matrix.NotBuildable.Single().Target.Should().Be(CrateTarget.AndroidArm64);
        matrix.NotBuildable.Single().Message.Should().Contain("x86-64 Docker engine");
    }
}
