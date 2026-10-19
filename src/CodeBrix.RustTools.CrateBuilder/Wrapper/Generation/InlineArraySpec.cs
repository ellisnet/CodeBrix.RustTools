namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// One generated <c>[InlineArray]</c> struct standing for a Rust fixed-size array
/// (<c>[f64; 3]</c> -&gt; <c>DoubleArray3</c>; <c>[[f64; 3]; 3]</c> -&gt;
/// <c>DoubleArray3x3</c>, an array of three <c>DoubleArray3</c>).
/// </summary>
internal sealed class InlineArraySpec
{
    /// <summary>The struct name.</summary>
    public string Name { get; set; }

    /// <summary>The element count.</summary>
    public int Length { get; set; }

    /// <summary>The C# element type.</summary>
    public string ElementType { get; set; }

    /// <summary>The Rust spelling of the array type.</summary>
    public string RustType { get; set; }
}
