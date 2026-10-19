using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class ConstantLiteralsTests
{
    [Theory]
    [InlineData("u32", "0xFF_00", "uint", "65280u")]
    [InlineData("u32", "7u32", "uint", "7u")]
    [InlineData("i32", "-5", "int", "-5")]
    [InlineData("i64", "1_000_000", "long", "1000000L")]
    [InlineData("u64", "0b1010", "ulong", "10ul")]
    [InlineData("u8", "0o17", "byte", "(byte)15")]
    [InlineData("i16", "-7", "short", "(short)(-7)")]
    [InlineData("usize", "1_000", "nuint", "(nuint)1000")]
    [InlineData("isize", "-1", "nint", "(nint)(-1)")]
    [InlineData("f64", "2.5", "double", "2.5d")]
    [InlineData("f64", "1e-6", "double", "1e-6d")]
    [InlineData("f32", "1.", "float", "1.0f")]
    [InlineData("f32", "0.25f32", "float", "0.25f")]
    [InlineData("bool", "true", "bool", "true")]
    [InlineData("&str", "\"plain\"", "string", "\"plain\"")]
    [InlineData("&'static str", "\"a\\\"b\\n\\u{41}\\x41\"", "string", "\"a\\\"b\\nAA\"")]
    [InlineData("&str", "r#\"raw \"quoted\" \\n\"#", "string", "\"raw \\\"quoted\\\" \\\\n\"")]
    public void TryConvert_turns_a_rust_literal_into_the_equal_csharp_constant(string rustType, string value, string expectedType, string expectedLiteral)
    {
        //Act
        var ok = ConstantLiterals.TryConvert(rustType, value, out var managedType, out var literal, out var why);

        //Assert
        ok.Should().BeTrue(why);
        managedType.Should().Be(expectedType);
        literal.Should().Be(expectedLiteral);
    }

    [Theory]
    [InlineData("u8", "256", "does not fit u8")]
    [InlineData("usize", "0x1_0000_0000", "does not fit 32 bits")]
    [InlineData("u32", null, "not a plain literal")]
    [InlineData("u32", "1.5", "not a plain integer literal")]
    [InlineData("char", "'x'", "has no C# constant form")]
    [InlineData("&str", "b\"bytes\"", "not a plain string literal")]
    [InlineData("&str", "\"\\q\"", "not a plain string literal")]
    [InlineData("bool", "1", "not true or false")]
    public void TryConvert_refuses_what_has_no_exact_csharp_constant(string rustType, string value, string expectedReason)
    {
        //Act
        var ok = ConstantLiterals.TryConvert(rustType, value, out _, out _, out var why);

        //Assert
        ok.Should().BeFalse();
        why.Should().Contain(expectedReason);
    }

    [Theory]
    [InlineData("MAX_SIZE", "MaxSize")]
    [InlineData("SCALE", "Scale")]
    [InlineData("DEFAULT_FPS_2", "DefaultFps2")]
    [InlineData("chunk_len", "ChunkLen")]
    public void ConstantName_turns_screaming_case_into_pascal_case(string rustName, string expected)
        => PlannerRun.ConstantName(rustName).Should().Be(expected);
}
