namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The wrapper project in a <see cref="BuildRecord"/>.
/// </summary>
public sealed class WrapperRecord
{
    /// <summary>The wrapper's assembly name.</summary>
    public string AssemblyName { get; set; }

    /// <summary>The wrapper's root namespace.</summary>
    public string RootNamespace { get; set; }

    /// <summary>The package id, or <c>null</c> for a plain class library.</summary>
    public string PackageId { get; set; }

    /// <summary>The wrapper's entry class (<see cref="WrapperResult.EntryClassName"/>), for
    /// example <c>Datacrate</c> or <c>DatacrateLibrary</c>.</summary>
    public string EntryClassName { get; set; }

    /// <summary>The path of the wrapper's .csproj.</summary>
    public string ProjectPath { get; set; }
}
