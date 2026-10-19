using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A generic type parameter.
/// </summary>
internal sealed class RustTypeParam
{
    /// <summary>The parameter's name (<c>T</c>).</summary>
    public string Name { get; set; }

    /// <summary>Its bounds, verbatim (<c>PartialOrd</c>).</summary>
    public List<string> Bounds { get; set; } = new List<string>();

    /// <summary>The default, verbatim; <c>null</c> when none.</summary>
    public string Default { get; set; }
}
