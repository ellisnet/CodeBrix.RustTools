using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A field of a struct, union or enum variant.
/// </summary>
internal sealed class RustField
{
    /// <summary>The field's name; <c>null</c> for a tuple field.</summary>
    public string Name { get; set; }

    /// <summary>The 0-based position.</summary>
    public int Index { get; set; }

    /// <summary>The field's type.</summary>
    public RustType Type { get; set; }

    /// <summary><c>pub</c>, <c>pub(crate)</c>, ... or <c>private</c>.</summary>
    public string Visibility { get; set; }

    /// <summary>The field's own <c>cfg</c> predicates.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The field's doc comments.</summary>
    public string Docs { get; set; } = string.Empty;

    /// <summary>Other attributes, verbatim.</summary>
    public List<string> Attributes { get; set; } = new List<string>();

    /// <summary>Whether the field is <c>pub</c>.</summary>
    [JsonIgnore]
    public bool IsPublic => string.Equals(Visibility, "pub", StringComparison.Ordinal);
}
