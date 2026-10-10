using CodeBrix.RustTools.Building;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoProgressTests
{
    [Fact]
    public void Fraction_is_null_without_a_total()
        => new CargoProgress { Completed = 3 }.Fraction.Should().BeNull();

    [Fact]
    public void Fraction_is_capped_at_one()
        => new CargoProgress { Completed = 12, Total = 10 }.Fraction.Should().Be(1.0);

    [Fact]
    public void ToString_shows_counts_and_message()
        => new CargoProgress { Completed = 2, Total = 5, Message = "Compiling a v1.0.0" }.ToString().Should().Be("[2/5] Compiling a v1.0.0");
}
