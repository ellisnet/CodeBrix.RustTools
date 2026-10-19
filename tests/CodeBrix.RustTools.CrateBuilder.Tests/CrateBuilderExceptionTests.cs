using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests;

public class CrateBuilderExceptionTests
{
    [Fact]
    public void every_exception_derives_from_the_family_base_and_carries_its_stage()
    {
        //Act
        var inner = new InvalidOperationException("inner");
        var exceptions = new CrateBuilderException[]
        {
            new SourceResolutionException("a", inner),
            new StagingException("b"),
            new FacadeGenerationException("c", CrateBuildStage.Planning),
            new WrapperGenerationException("d"),
            new ArtifactVerificationException("e"),
        };

        //Assert
        exceptions.Should().OnlyContain(e => e is RustToolsException);
        exceptions[0].Stage.Should().Be(CrateBuildStage.ResolvingSource);
        exceptions[0].InnerException.Should().BeSameAs(inner);
        exceptions[1].Stage.Should().Be(CrateBuildStage.Staging);
        exceptions[2].Stage.Should().Be(CrateBuildStage.Planning);
        exceptions[3].Stage.Should().Be(CrateBuildStage.Wrapping);
        exceptions[4].Stage.Should().Be(CrateBuildStage.Verifying);
        new FacadeGenerationException("f").Stage.Should().Be(CrateBuildStage.RenderingFacade);
    }

    [Fact]
    public void a_base_exception_without_a_stage_takes_the_first_one_assigned()
    {
        //Arrange
        var exception = new CrateBuilderException("x");

        //Act
        exception.AssignStageIfUnknown(CrateBuildStage.Building);
        exception.AssignStageIfUnknown(CrateBuildStage.Wrapping);

        //Assert
        exception.Stage.Should().Be(CrateBuildStage.Building);
        exception.HasStage.Should().BeTrue();
    }
}
