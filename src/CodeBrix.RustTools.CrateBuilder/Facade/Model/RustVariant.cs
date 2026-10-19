using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// An enum variant.
/// </summary>
internal sealed class RustVariant
{
    /// <summary>The variant's name.</summary>
    public string Name { get; set; }

    /// <summary>Unit, tuple or named.</summary>
    public RustItemShape Shape { get; set; }

    /// <summary>The variant's fields.</summary>
    public List<RustField> Fields { get; set; } = new List<RustField>();

    /// <summary>The explicit discriminant expression verbatim; <c>null</c> when none.</summary>
    public string Discriminant { get; set; }

    /// <summary>The discriminant when it is an integer literal (optionally negative).</summary>
    public long? DiscriminantValue { get; set; }

    /// <summary>The variant's own <c>cfg</c> predicates.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The variant's doc comments.</summary>
    public string Docs { get; set; } = string.Empty;

    /// <summary>Other attributes, verbatim.</summary>
    public List<string> Attributes { get; set; } = new List<string>();
}
