using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A struct or union.
/// </summary>
internal sealed class RustStruct : RustItem
{
    /// <summary>Named, tuple, unit or union.</summary>
    public RustItemShape Shape { get; set; }

    /// <summary>All fields, public or not.</summary>
    public List<RustField> Fields { get; set; } = new List<RustField>();

    /// <summary>The number of fields of a tuple struct; 0 otherwise.</summary>
    public int TupleArity { get; set; }

    /// <summary><c>#[derive(...)]</c> paths as written.</summary>
    public List<string> Derives { get; set; } = new List<string>();

    /// <summary><c>#[repr(...)]</c> hints verbatim (<c>C</c>, <c>transparent</c>).</summary>
    public List<string> Repr { get; set; } = new List<string>();

    /// <summary>The struct's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary>A type or const parameter.</summary>
    public bool HasGenerics { get; set; }

    /// <summary>Derived and implemented trait names, sorted (<c>Clone</c>, <c>Copy</c>,
    /// <c>Display</c>; <c>!Send</c> for a negative impl).</summary>
    public List<string> ImplementedTraits { get; set; } = new List<string>();

    /// <summary>Whether the struct implements (or derives) <paramref name="traitName"/>.</summary>
    public bool Implements(string traitName) => ImplementedTraits.Contains(traitName, StringComparer.Ordinal);

    /// <summary>Whether it is <c>Copy</c>.</summary>
    [JsonIgnore]
    public bool IsCopy => Implements("Copy");

    /// <summary>Whether every field is <c>pub</c> (true for a unit struct).</summary>
    [JsonIgnore]
    public bool AllFieldsPublic => Fields.All(f => f.IsPublic);

    /// <summary>Whether it is <c>#[repr(C)]</c>.</summary>
    [JsonIgnore]
    public bool IsReprC => Repr.Contains("C", StringComparer.Ordinal);
}
