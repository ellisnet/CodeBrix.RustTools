using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The planner's structural reading of one Rust type (aliases resolved, local types looked up):
/// what it is for the C boundary, or why it cannot cross it.
/// </summary>
internal sealed class PlannedType
{
    /// <summary>What the type is.</summary>
    public PlannedTypeKind Kind { get; set; }

    /// <summary>The type as written.</summary>
    public string Text { get; set; }

    /// <summary><see cref="PlannedTypeKind.Primitive"/>: the Rust primitive name (<c>u8</c>).</summary>
    public string Primitive { get; set; }

    /// <summary>References, pointers, slices: <c>&amp;mut</c> / <c>*mut</c> / <c>&amp;mut [T]</c>.</summary>
    public bool Mutable { get; set; }

    /// <summary>The referenced / element / optional / Ok type.</summary>
    public PlannedType Inner { get; set; }

    /// <summary>Tuple elements.</summary>
    public List<PlannedType> Items { get; set; } = new List<PlannedType>();

    /// <summary>Array length.</summary>
    public int Length { get; set; }

    /// <summary>The mirror of a mirror struct.</summary>
    public MirrorStruct Mirror { get; set; }

    /// <summary>The handle of a handle type.</summary>
    public HandleType Handle { get; set; }

    /// <summary>The enum mapping of a C-like enum.</summary>
    public EnumMapping Enum { get; set; }

    /// <summary>The <c>Err</c> type of a <c>Result</c>, as written.</summary>
    public RustType ErrorType { get; set; }

    /// <summary>The local item an error type resolved to, when it did.</summary>
    public RustItem ErrorItem { get; set; }

    /// <summary><see cref="PlannedTypeKind.Unsupported"/>: a noun phrase for the novice
    /// (<c>a callback (impl FnMut(i32))</c>).</summary>
    public string Description { get; set; }

    /// <summary><see cref="PlannedTypeKind.Unsupported"/>: an optional extra sentence of advice.</summary>
    public string Advice { get; set; }

    /// <summary>For a <see cref="PlannedTypeKind.Str"/> or <see cref="PlannedTypeKind.String"/>
    /// that is a standard-library path or OS string: <c>Path</c>, <c>PathBuf</c>, <c>OsStr</c>
    /// or <c>OsString</c> (crossing C as UTF-8 text); <c>null</c> for <c>str</c> /
    /// <c>String</c>.</summary>
    public string StdText { get; set; }

    /// <summary>An unsupported type with the given description.</summary>
    public static PlannedType Unsupported(string text, string description, string advice = null)
        => new PlannedType { Kind = PlannedTypeKind.Unsupported, Text = text, Description = description, Advice = advice };

    /// <summary>The first unsupported type in this one (itself, or a nested one), or <c>null</c>.</summary>
    public PlannedType FirstUnsupported()
    {
        if (Kind == PlannedTypeKind.Unsupported)
        {
            return this;
        }

        var inner = Inner?.FirstUnsupported();
        if (inner != null)
        {
            return inner;
        }

        foreach (var item in Items)
        {
            var nested = item.FirstUnsupported();
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Every handle mentioned in this type.</summary>
    public IEnumerable<HandleType> Handles()
    {
        if (Handle != null)
        {
            yield return Handle;
        }

        if (Inner != null)
        {
            foreach (var h in Inner.Handles())
            {
                yield return h;
            }
        }

        foreach (var item in Items)
        {
            foreach (var h in item.Handles())
            {
                yield return h;
            }
        }
    }
}
