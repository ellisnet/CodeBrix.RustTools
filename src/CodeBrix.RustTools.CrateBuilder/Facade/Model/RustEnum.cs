using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// An enum.
/// </summary>
internal sealed class RustEnum : RustItem
{
    /// <summary>The variants, in declaration order.</summary>
    public List<RustVariant> Variants { get; set; } = new List<RustVariant>();

    /// <summary>Every variant is unit-shaped.</summary>
    public bool IsCLike { get; set; }

    /// <summary><c>#[derive(...)]</c> paths as written.</summary>
    public List<string> Derives { get; set; } = new List<string>();

    /// <summary><c>#[repr(...)]</c> hints verbatim (<c>i32</c>, <c>u8</c>, <c>C</c>).</summary>
    public List<string> Repr { get; set; } = new List<string>();

    /// <summary>The enum's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary>A type or const parameter.</summary>
    public bool HasGenerics { get; set; }

    /// <summary>Derived and implemented trait names, sorted.</summary>
    public List<string> ImplementedTraits { get; set; } = new List<string>();

    /// <summary>Whether the enum implements (or derives) <paramref name="traitName"/>.</summary>
    public bool Implements(string traitName) => ImplementedTraits.Contains(traitName, StringComparer.Ordinal);

    /// <summary>Whether it is <c>Copy</c>.</summary>
    [JsonIgnore]
    public bool IsCopy => Implements("Copy");

    /// <summary>Whether a variant carries data.</summary>
    [JsonIgnore]
    public bool HasDataVariants => Variants.Any(v => v.Shape != RustItemShape.Unit);

    /// <summary>The variant named <paramref name="name"/>, or <c>null</c>.</summary>
    public RustVariant FindVariant(string name) => Variants.FirstOrDefault(v => v.Name == name);
}
