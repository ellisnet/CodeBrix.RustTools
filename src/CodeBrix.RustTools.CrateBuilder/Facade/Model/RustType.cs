using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A Rust type, structurally (SCHEMA.md "Types"). Only the properties of its
/// <see cref="Kind"/> are set; the others are <c>null</c>.
/// </summary>
internal sealed class RustType
{
    /// <summary>The structural kind.</summary>
    public RustTypeKind Kind { get; set; }

    /// <summary>The type as written (rustfmt-like spacing), for example <c>&amp;[u8]</c>.</summary>
    public string Text { get; set; }

    /// <summary><see cref="RustTypeKind.Primitive"/>: the primitive's name.</summary>
    public string Name { get; set; }

    /// <summary><see cref="RustTypeKind.Reference"/> / <see cref="RustTypeKind.Ptr"/>:
    /// <c>&amp;mut</c> / <c>*mut</c>.</summary>
    public bool? Mutable { get; set; }

    /// <summary><see cref="RustTypeKind.Reference"/>: the lifetime when written (<c>'a</c>).</summary>
    public string Lifetime { get; set; }

    /// <summary><see cref="RustTypeKind.Reference"/> / <see cref="RustTypeKind.Ptr"/>: the
    /// referenced type.</summary>
    public RustType Inner { get; set; }

    /// <summary><see cref="RustTypeKind.Slice"/> / <see cref="RustTypeKind.Array"/>: the
    /// element type.</summary>
    public RustType Elem { get; set; }

    /// <summary><see cref="RustTypeKind.Array"/>: the length expression, verbatim.</summary>
    public string Len { get; set; }

    /// <summary><see cref="RustTypeKind.Array"/>: the length when it is an integer
    /// literal.</summary>
    public ulong? LenValue { get; set; }

    /// <summary><see cref="RustTypeKind.Path"/>: the <c>T</c> of <c>&lt;T as Trait&gt;::X</c>.</summary>
    public RustType Qself { get; set; }

    /// <summary><see cref="RustTypeKind.Path"/>: the segments.</summary>
    public List<RustPathSegment> Segments { get; set; }

    /// <summary><see cref="RustTypeKind.Tuple"/>: the element types.</summary>
    public List<RustType> Elems { get; set; }

    /// <summary><see cref="RustTypeKind.ImplTrait"/> / <see cref="RustTypeKind.DynTrait"/>:
    /// the bounds, verbatim.</summary>
    public List<string> Bounds { get; set; }

    /// <summary><see cref="RustTypeKind.FnPtr"/>: the parameter types.</summary>
    public List<RustType> Inputs { get; set; }

    /// <summary><see cref="RustTypeKind.FnPtr"/>: the return type (unit when none).</summary>
    public RustType Output { get; set; }

    /// <summary><see cref="RustTypeKind.FnPtr"/>: <c>unsafe fn</c>.</summary>
    public bool? IsUnsafe { get; set; }

    /// <summary><see cref="RustTypeKind.FnPtr"/>: the ABI of an <c>extern</c> function
    /// pointer.</summary>
    public string Abi { get; set; }

    /// <summary><see cref="RustTypeKind.Path"/>: the canonical path of the struct, enum or type
    /// alias of this crate the path names, resolved by the extractor in the module where the
    /// type was written (its <c>use</c> declarations included); <c>null</c> when it names
    /// something else or could not be resolved to exactly one item.</summary>
    public string Resolved { get; set; }

    /// <summary><see cref="RustTypeKind.Path"/>: <c>true</c> when the path's first segment is
    /// imported from another crate (or std) by a <c>use</c> in the module where the type was
    /// written, so it never names an item of this crate.</summary>
    public bool? External { get; set; }

    /// <summary><see cref="RustTypeKind.Path"/>: <c>true</c> when the path's first segment
    /// names nothing declared or imported in the module where the type was written (a
    /// prelude or std type such as <c>Result</c>, or a generic parameter).</summary>
    public bool? Unresolved { get; set; }

    /// <summary>Whether this is <c>()</c>.</summary>
    [JsonIgnore]
    public bool IsUnit => Kind == RustTypeKind.Unit;

    /// <summary>Whether this is a primitive.</summary>
    [JsonIgnore]
    public bool IsPrimitive => Kind == RustTypeKind.Primitive;

    /// <summary>Whether this is the primitive <paramref name="name"/> (<c>"u8"</c>).</summary>
    public bool IsPrimitiveNamed(string name) => Kind == RustTypeKind.Primitive && string.Equals(Name, name, StringComparison.Ordinal);

    /// <summary>Whether this is <c>&amp;str</c> (any lifetime, not <c>&amp;mut</c>).</summary>
    [JsonIgnore]
    public bool IsStrReference => Kind == RustTypeKind.Reference && Mutable != true && Inner != null && Inner.IsPrimitiveNamed("str");

    /// <summary>Whether this is <c>&amp;[T]</c> / <c>&amp;mut [T]</c>.</summary>
    [JsonIgnore]
    public bool IsSliceReference => Kind == RustTypeKind.Reference && Inner != null && Inner.Kind == RustTypeKind.Slice;

    /// <summary>Whether this is the path <c>Self</c>.</summary>
    /// <summary>Whether this is the bare one-segment path <paramref name="name"/> without
    /// type arguments (a generic parameter such as <c>P</c>).</summary>
    public bool IsSingleName(string name) => Kind == RustTypeKind.Path && Qself == null && Segments != null && Segments.Count == 1 && Segments[0].Name == name && (Segments[0].Args == null || Segments[0].Args.Count == 0);

    [JsonIgnore]
    public bool IsSelf => Kind == RustTypeKind.Path && Qself == null && Segments != null && Segments.Count == 1 && Segments[0].Name == "Self";

    /// <summary>The last path segment, or <c>null</c> when this is not a path.</summary>
    [JsonIgnore]
    public RustPathSegment LastSegment => Kind == RustTypeKind.Path && Segments != null && Segments.Count > 0 ? Segments[Segments.Count - 1] : null;

    /// <summary>The last path segment's name (<c>Option</c> for <c>std::option::Option&lt;T&gt;</c>),
    /// or <c>null</c>.</summary>
    [JsonIgnore]
    public string PathName => LastSegment?.Name;

    /// <summary>The type arguments of the last path segment (empty when none).</summary>
    [JsonIgnore]
    public IReadOnlyList<RustType> GenericArguments => (IReadOnlyList<RustType>)LastSegment?.Args ?? Array.Empty<RustType>();

    /// <summary>Whether this is a path whose last segment is <paramref name="name"/> (for
    /// example <c>IsPathNamed("Option")</c>).</summary>
    public bool IsPathNamed(string name) => string.Equals(PathName, name, StringComparison.Ordinal);

    /// <summary>The path written as segment names (<c>std::rc::Rc</c>), without arguments, or
    /// <c>null</c>.</summary>
    [JsonIgnore]
    public string PathWithoutArguments => Kind == RustTypeKind.Path && Segments != null ? string.Join("::", Segments.Select(s => s.Name)) : null;

    /// <summary>Every type nested in this one, this one first (depth first).</summary>
    public IEnumerable<RustType> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children())
        {
            foreach (var nested in child.DescendantsAndSelf())
            {
                yield return nested;
            }
        }
    }

    private IEnumerable<RustType> Children()
    {
        if (Inner != null)
        {
            yield return Inner;
        }

        if (Elem != null)
        {
            yield return Elem;
        }

        if (Qself != null)
        {
            yield return Qself;
        }

        foreach (var t in Elems ?? Enumerable.Empty<RustType>())
        {
            yield return t;
        }

        foreach (var t in Inputs ?? Enumerable.Empty<RustType>())
        {
            yield return t;
        }

        if (Output != null)
        {
            yield return Output;
        }

        foreach (var segment in Segments ?? Enumerable.Empty<RustPathSegment>())
        {
            foreach (var t in segment.Args)
            {
                yield return t;
            }

            foreach (var b in segment.Bindings)
            {
                yield return b.Type;
            }

            foreach (var t in segment.FnInputs ?? Enumerable.Empty<RustType>())
            {
                yield return t;
            }

            if (segment.FnOutput != null)
            {
                yield return segment.FnOutput;
            }
        }
    }

    /// <summary>The type printed from its structure (<see cref="RustTypePrinter"/>).</summary>
    public override string ToString() => RustTypePrinter.Print(this);
}
