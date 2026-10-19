using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A module of the crate (the root is <c>crate</c>), public or not.
/// </summary>
internal sealed class RustModule
{
    /// <summary>The module path (<c>crate::shapes</c>).</summary>
    public string Path { get; set; }

    /// <summary>The last segment (<c>crate</c> for the root).</summary>
    public string Name { get; set; }

    /// <summary><c>pub</c>, <c>pub(crate)</c>, ... or <c>private</c>.</summary>
    public string Visibility { get; set; }

    /// <summary>The file holding its items, relative.</summary>
    public string File { get; set; }

    /// <summary>Declared inline (<c>mod x { ... }</c>).</summary>
    public bool IsInline { get; set; }

    /// <summary>Every <c>cfg</c> predicate that applies.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The module's docs.</summary>
    public string Docs { get; set; } = string.Empty;

    /// <summary>Paths by which outside code can name the module.</summary>
    public List<string> PublicPaths { get; set; } = new List<string>();
}
