using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Generic parameters and the where clause of an item.
/// </summary>
internal sealed class RustGenerics
{
    /// <summary>The parameter list verbatim (<c>&lt;T: Clone&gt;</c>); empty when none.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The where clause verbatim; empty when none.</summary>
    public string WhereClause { get; set; } = string.Empty;

    /// <summary>Lifetime parameters (<c>'a</c>).</summary>
    public List<string> Lifetimes { get; set; } = new List<string>();

    /// <summary>Type parameters.</summary>
    public List<RustTypeParam> TypeParams { get; set; } = new List<RustTypeParam>();

    /// <summary>Const parameters.</summary>
    public List<RustConstParam> ConstParams { get; set; } = new List<RustConstParam>();

    /// <summary>Whether there is at least one type or const parameter.</summary>
    [JsonIgnore]
    public bool HasTypeOrConstParams => TypeParams.Count > 0 || ConstParams.Count > 0;
}
