using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// One name of a <c>pub use</c> declaration.
/// </summary>
internal sealed class RustReexport
{
    /// <summary>The module holding the <c>use</c>.</summary>
    public string Module { get; set; }

    /// <summary>Always <c>pub</c>.</summary>
    public string Visibility { get; set; }

    /// <summary>The path as written, without a rename (<c>shapes::Rect</c>).</summary>
    public string Path { get; set; }

    /// <summary>The exported name (the rename with <c>as</c>), <c>*</c> for a glob.</summary>
    public string Name { get; set; }

    /// <summary><c>use path::*</c>.</summary>
    public bool IsGlob { get; set; }

    /// <summary>Canonical paths of the local items / modules it resolved to, or the external
    /// path.</summary>
    public List<string> Targets { get; set; } = new List<string>();

    /// <summary>Every target is outside the crate.</summary>
    public bool IsExternal { get; set; }

    /// <summary>Every <c>cfg</c> predicate that applies.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The source file, relative.</summary>
    public string File { get; set; }

    /// <summary>The 1-based line.</summary>
    public int Line { get; set; }
}
