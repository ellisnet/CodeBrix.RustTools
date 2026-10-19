namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The tool that wrote the document.
/// </summary>
internal sealed class RustToolInfo
{
    /// <summary><c>codebrix-api-extractor</c>.</summary>
    public string Name { get; set; }

    /// <summary>The tool's version.</summary>
    public string Version { get; set; }
}
