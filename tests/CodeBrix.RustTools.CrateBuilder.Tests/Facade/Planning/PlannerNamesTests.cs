using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class PlannerNamesTests
{
    [Theory]
    [InlineData("EncoderConfig", "encoder_config")]
    [InlineData("Vec2", "vec2")]
    [InlineData("HTTPServer", "http_server")]
    [InlineData("parse_pair", "parse_pair")]
    [InlineData("r#type", "type")]
    public void Snake_converts_type_names(string name, string expected) => PlannerNames.Snake(name).Should().Be(expected);

    [Theory]
    [InlineData("from_seconds_f64", "FromSecondsF64")]
    [InlineData("Vec2", "Vec2")]
    [InlineData("frame_size", "FrameSize")]
    [InlineData("_private", "Private")]
    public void Pascal_converts_rust_identifiers(string name, string expected) => PlannerNames.Pascal(name).Should().Be(expected);

    [Theory]
    [InlineData("frame_size", "frameSize")]
    [InlineData("in", "@in")]
    [InlineData("object", "@object")]
    public void Camel_escapes_csharp_keywords(string name, string expected) => PlannerNames.Camel(name).Should().Be(expected);

    [Theory]
    [InlineData("default", "default_")]
    [InlineData("type", "type_")]
    [InlineData("data", "data")]
    public void SafeC_avoids_c_and_rust_keywords(string name, string expected) => PlannerNames.SafeC(name).Should().Be(expected);

    [Fact]
    public void Unique_appends_numbers_from_two()
    {
        //Arrange
        var taken = new HashSet<string> { "a_b" };

        //Act
        var first = PlannerNames.Unique("a_b", taken, "_");
        var second = PlannerNames.Unique("a_b", taken, "_");

        //Assert
        first.Should().Be("a_b_2");
        second.Should().Be("a_b_3");
    }
}
