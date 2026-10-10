using System;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Builds;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Builds;

public class ContainerBuildProgressTests
{
    [Fact]
    public void BuildStage_lists_the_stages_in_pipeline_order()
        => Enum.GetValues<BuildStage>().Select(s => s.ToString()).Should().Equal(
            "CheckingEngine", "EnsuringImage", "StartingContainer", "CopyingSource", "AuditingLicenses",
            "Building", "RetrievingArtifacts", "CleaningUp", "Completed", "Failed");

    [Fact]
    public void ToString_shows_the_stage_and_message()
        => new ContainerBuildProgress { Stage = BuildStage.EnsuringImage, Message = "Pulling rust:latest" }
            .ToString().Should().Be("[EnsuringImage] Pulling rust:latest");

    [Fact]
    public void ToString_shows_the_cargo_progress_while_building()
        => new ContainerBuildProgress
        {
            Stage = BuildStage.Building,
            CargoProgress = new CargoProgress { Completed = 3, Total = 10, Message = "Compiling serde v1.0.228" },
        }.ToString().Should().Be("[Building] [3/10] Compiling serde v1.0.228");

    [Fact]
    public void ToString_shows_only_the_stage_without_a_message()
        => new ContainerBuildProgress { Stage = BuildStage.Completed }.ToString().Should().Be("[Completed]");

    [Fact]
    public void defaults_have_no_cargo_progress_or_percent()
    {
        //Act
        var progress = new ContainerBuildProgress();

        //Assert
        progress.CargoProgress.Should().BeNull();
        progress.Percent.Should().BeNull();
        progress.Message.Should().BeEmpty();
    }
}
