using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A trait (informational).
/// </summary>
internal sealed class RustTrait : RustItem
{
    /// <summary>The trait's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary>A type or const parameter.</summary>
    public bool HasGenerics { get; set; }

    /// <summary>Supertraits, verbatim.</summary>
    public List<string> Supertraits { get; set; } = new List<string>();

    /// <summary><c>unsafe trait</c>.</summary>
    public bool IsUnsafe { get; set; }

    /// <summary><c>auto trait</c>.</summary>
    public bool IsAuto { get; set; }

    /// <summary>The trait's methods (no owner; <see cref="RustFunction.HasDefault"/> set).</summary>
    public List<RustFunction> Methods { get; set; } = new List<RustFunction>();

    /// <summary>Names of the associated types.</summary>
    public List<string> AssociatedTypes { get; set; } = new List<string>();

    /// <summary>Names of the associated consts.</summary>
    public List<string> AssociatedConsts { get; set; } = new List<string>();
}
