using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;

public class RustDocTextTests
{
    [Fact]
    public void Paragraphs_split_on_blank_lines_and_join_lines()
        => RustDocText.Paragraphs("First line\nsecond line.\n\n  Next   \npara.\n").Should().Equal("First line second line.", "Next para.");

    [Fact]
    public void Summary_is_the_first_paragraph_or_empty()
    {
        //Assert
        RustDocText.Summary("A.\n\nB.").Should().Be("A.");
        RustDocText.Summary(null).Should().Be(string.Empty);
    }

    [Fact]
    public void Mentions_finds_a_unit_in_the_fixture_docs()
    {
        //Arrange
        var tick = ModelSamples.Load(ModelSamples.DataCrate).FindStruct("Tick");

        //Assert
        RustDocText.Mentions(tick.Docs, "ticks per second = 254016000000").Should().BeTrue();
        RustDocText.Mentions(tick.Docs, "nanoseconds").Should().BeFalse();
    }
}
