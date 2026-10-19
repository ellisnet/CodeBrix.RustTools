using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// The native files of one RID in the wrapper's runtimes/ folder.
/// </summary>
internal sealed class RuntimeModel
{
    /// <summary>The RID.</summary>
    public string Rid { get; set; }

    /// <summary>The file the loader loads.</summary>
    public string FileName { get; set; }

    /// <summary>Every file of that RID (file names).</summary>
    public List<string> Files { get; set; } = new List<string>();

    /// <summary>A note for the README (for example a development-only macOS build), or
    /// <c>null</c>.</summary>
    public string Note { get; set; }
}
