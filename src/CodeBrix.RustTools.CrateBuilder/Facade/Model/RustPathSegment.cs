using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// One segment of a path type (<c>Vec&lt;u8&gt;</c>, <c>Fn(i32) -&gt; i32</c>).
/// </summary>
internal sealed class RustPathSegment
{
    /// <summary>The identifier.</summary>
    public string Name { get; set; }

    /// <summary>Type arguments in <c>&lt;...&gt;</c>.</summary>
    public List<RustType> Args { get; set; } = new List<RustType>();

    /// <summary>Lifetime arguments (<c>'a</c>).</summary>
    public List<string> Lifetimes { get; set; } = new List<string>();

    /// <summary>Const arguments written as expressions or blocks, verbatim.</summary>
    public List<string> ConstArgs { get; set; } = new List<string>();

    /// <summary>Associated type bindings (<c>Item = i32</c>).</summary>
    public List<RustAssocBinding> Bindings { get; set; } = new List<RustAssocBinding>();

    /// <summary>Other generic arguments (constraints, associated consts), verbatim.</summary>
    public List<string> OtherArgs { get; set; } = new List<string>();

    /// <summary>Parenthesized arguments (<c>Fn(A, B)</c>); <c>null</c> otherwise.</summary>
    public List<RustType> FnInputs { get; set; }

    /// <summary>The output of parenthesized arguments (unit when none); <c>null</c>
    /// otherwise.</summary>
    public RustType FnOutput { get; set; }
}
