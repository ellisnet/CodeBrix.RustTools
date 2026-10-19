using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// An <c>impl Trait for Type</c> block (informational: whether a type implements
/// <c>Default</c>, <c>Clone</c>, <c>Copy</c>, <c>Drop</c>, <c>Display</c> ... matters to the
/// planner).
/// </summary>
internal sealed class RustImplInfo
{
    /// <summary>The module holding the impl.</summary>
    public string Module { get; set; }

    /// <summary>The source file, relative.</summary>
    public string File { get; set; }

    /// <summary>The 1-based line of the self type.</summary>
    public int Line { get; set; }

    /// <summary>Every <c>cfg</c> predicate that applies to the impl.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The trait as written (<c>fmt::Display</c>).</summary>
    public string TraitPath { get; set; }

    /// <summary>The trait's last segment (<c>Display</c>).</summary>
    public string TraitName { get; set; }

    /// <summary>The implementing type.</summary>
    public RustType SelfType { get; set; }

    /// <summary>The canonical path of the local type it targets; <c>null</c> when not
    /// resolved (external or non-path types).</summary>
    public string SelfPath { get; set; }

    /// <summary>The impl block's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary><c>unsafe impl</c>.</summary>
    public bool IsUnsafe { get; set; }

    /// <summary><c>impl !Trait for Type</c>.</summary>
    public bool IsNegative { get; set; }

    /// <summary>Names of the methods it defines.</summary>
    public List<string> Methods { get; set; } = new List<string>();

    /// <summary>Names of the associated types it defines.</summary>
    public List<string> AssociatedTypes { get; set; } = new List<string>();
}
