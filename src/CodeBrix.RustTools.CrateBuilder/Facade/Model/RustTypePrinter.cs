using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Prints a <see cref="RustType"/> from its structure, in rustfmt's spelling
/// (<c>&amp;mut [u8]</c>, <c>Option&lt;usize&gt;</c>, <c>[[f64; 3]; 3]</c>,
/// <c>impl FnMut(i32)</c>, <c>extern "C" fn(i32) -&gt; i32</c>). For the extractor's own
/// documents the result equals <see cref="RustType.Text"/> except where the structure does not
/// keep a detail (the trait of a qualified path, a leading <c>::</c>, the original order of
/// mixed lifetime / type arguments).
/// </summary>
internal static class RustTypePrinter
{
    /// <summary>The type as Rust source text; empty for <c>null</c>.</summary>
    public static string Print(RustType type)
    {
        if (type == null)
        {
            return string.Empty;
        }

        switch (type.Kind)
        {
            case RustTypeKind.Primitive:
                return type.Name ?? type.Text;
            case RustTypeKind.Unit:
                return "()";
            case RustTypeKind.Never:
                return "!";
            case RustTypeKind.Infer:
                return "_";
            case RustTypeKind.Reference:
                return "&" + (type.Lifetime != null ? type.Lifetime + " " : string.Empty) + (type.Mutable == true ? "mut " : string.Empty) + Print(type.Inner);
            case RustTypeKind.Ptr:
                return "*" + (type.Mutable == true ? "mut " : "const ") + Print(type.Inner);
            case RustTypeKind.Slice:
                return "[" + Print(type.Elem) + "]";
            case RustTypeKind.Array:
                return "[" + Print(type.Elem) + "; " + (type.Len ?? (type.LenValue?.ToString() ?? "_")) + "]";
            case RustTypeKind.Tuple:
                var elems = type.Elems ?? new List<RustType>();
                return "(" + string.Join(", ", elems.Select(Print)) + (elems.Count == 1 ? "," : string.Empty) + ")";
            case RustTypeKind.ImplTrait:
                return "impl " + string.Join(" + ", type.Bounds ?? new List<string>());
            case RustTypeKind.DynTrait:
                return "dyn " + string.Join(" + ", type.Bounds ?? new List<string>());
            case RustTypeKind.FnPtr:
                var prefix = (type.IsUnsafe == true ? "unsafe " : string.Empty) + (type.Abi != null ? "extern \"" + type.Abi + "\" " : string.Empty);
                return prefix + "fn(" + string.Join(", ", (type.Inputs ?? new List<RustType>()).Select(Print)) + ")" + Arrow(type.Output);
            case RustTypeKind.Path:
                if (type.Qself != null || type.Segments == null || type.Segments.Count == 0)
                {
                    return type.Text;
                }

                return string.Join("::", type.Segments.Select(PrintSegment));
            default:
                return type.Text ?? string.Empty;
        }
    }

    private static string PrintSegment(RustPathSegment segment)
    {
        if (segment.FnInputs != null)
        {
            return segment.Name + "(" + string.Join(", ", segment.FnInputs.Select(Print)) + ")" + Arrow(segment.FnOutput);
        }

        var args = new List<string>();
        args.AddRange(segment.Lifetimes ?? Enumerable.Empty<string>());
        args.AddRange((segment.Args ?? new List<RustType>()).Select(Print));
        args.AddRange(segment.ConstArgs ?? Enumerable.Empty<string>());
        args.AddRange((segment.Bindings ?? new List<RustAssocBinding>()).Select(b => b.Name + " = " + Print(b.Type)));
        args.AddRange(segment.OtherArgs ?? Enumerable.Empty<string>());
        return args.Count == 0 ? segment.Name : segment.Name + "<" + string.Join(", ", args) + ">";
    }

    private static string Arrow(RustType output) => output == null || output.IsUnit ? string.Empty : " -> " + Print(output);
}
