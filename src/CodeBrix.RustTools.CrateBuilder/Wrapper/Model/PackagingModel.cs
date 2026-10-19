namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// The wrapper's NuGet metadata, every value already XML-escaped for the csproj.
/// </summary>
internal sealed class PackagingModel
{
    /// <summary>The package id.</summary>
    public string PackageId { get; set; }

    /// <summary>The SPDX licence expression.</summary>
    public string LicenseExpression { get; set; }

    /// <summary>The authors, or <c>null</c>.</summary>
    public string Authors { get; set; }

    /// <summary>The description.</summary>
    public string Description { get; set; }

    /// <summary>The project URL, or <c>null</c>.</summary>
    public string ProjectUrl { get; set; }

    /// <summary>The repository URL, or <c>null</c>.</summary>
    public string RepositoryUrl { get; set; }

    /// <summary>The copyright line, or <c>null</c>.</summary>
    public string Copyright { get; set; }

    /// <summary>The tags, ';'-separated, or <c>null</c>.</summary>
    public string Tags { get; set; }
}
