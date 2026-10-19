using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// One type in the generated API reference.
/// </summary>
internal sealed class ApiSectionModel
{
    /// <summary>The heading (for example "class Encoder : IDisposable").</summary>
    public string Title { get; set; }

    /// <summary>The type's summary.</summary>
    public string Summary { get; set; }

    /// <summary>The members.</summary>
    public List<ApiEntryModel> Entries { get; set; } = new List<ApiEntryModel>();
}
