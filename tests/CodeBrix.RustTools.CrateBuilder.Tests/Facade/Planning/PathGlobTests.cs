using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class PathGlobTests
{
    [Theory]
    [InlineData("geom::*", "geom::Point", true)]
    [InlineData("geom::*", "geom::shapes::Rect", true)]
    [InlineData("*::to_string", "Vec2::to_string", true)]
    [InlineData("*::to_string", "Vec2::to_str", false)]
    [InlineData("Vec2", "Vec2", true)]
    [InlineData("Vec2", "Vec2::dot", false)]
    [InlineData("*", "anything::at::all", true)]
    [InlineData("Enc*der", "Encoder", true)]
    [InlineData("enc*", "Encoder", false)]
    public void IsMatch_treats_star_as_any_run_of_characters(string pattern, string text, bool expected)
        => PathGlob.IsMatch(pattern, text).Should().Be(expected);

    [Fact]
    public void Forms_has_every_path_with_and_without_the_crate_prefix()
        => PathGlob.Forms(new[] { "crate::shapes::Rect", "crate::Rect" }).Should().Equal("crate::shapes::Rect", "shapes::Rect", "crate::Rect", "Rect");

    [Fact]
    public void AnyMatch_tries_every_form_and_ignores_blank_patterns()
        => PathGlob.AnyMatch(new[] { " ", "shapes::*" }, PathGlob.Forms(new[] { "crate::shapes::Rect" })).Should().BeTrue();
}
