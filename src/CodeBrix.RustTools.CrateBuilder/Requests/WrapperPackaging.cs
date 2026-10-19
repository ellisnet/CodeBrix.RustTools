using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// NuGet package metadata for the wrapper project. When it is given the wrapper csproj is
/// packable, carries a date-stamped version and packs the native files under
/// <c>runtimes/</c>; without it the wrapper is a plain class library.
/// </summary>
public sealed class WrapperPackaging
{
    /// <summary>The package id (REQUIRED).</summary>
    public string PackageId { get; set; }

    /// <summary>The SPDX licence expression of the package. Default <c>MIT</c>; it must
    /// allow the crate's own licence terms.</summary>
    public string LicenseExpression { get; set; } = "MIT";

    /// <summary>The package authors, or <c>null</c>.</summary>
    public string Authors { get; set; }

    /// <summary>The package description, or <c>null</c> for a generated one.</summary>
    public string Description { get; set; }

    /// <summary>The project URL, or <c>null</c>.</summary>
    public string ProjectUrl { get; set; }

    /// <summary>The repository URL, or <c>null</c>.</summary>
    public string RepositoryUrl { get; set; }

    /// <summary>The copyright line, or <c>null</c>.</summary>
    public string Copyright { get; set; }

    /// <summary>The package tags.</summary>
    public IList<string> Tags { get; set; } = new List<string>();

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="PackageId"/> is missing or not a valid
    /// package id, or <see cref="LicenseExpression"/> is blank.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PackageId))
        {
            throw new ArgumentException("WrapperPackaging.PackageId is required.");
        }

        var id = PackageId.Trim();
        if (id.Length > 100 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '_') || id.StartsWith('.') || id.EndsWith('.'))
        {
            throw new ArgumentException($"WrapperPackaging.PackageId '{PackageId}' is not a valid NuGet package id (letters, digits, '.', '-' and '_'; at most 100 characters).");
        }

        if (string.IsNullOrWhiteSpace(LicenseExpression))
        {
            throw new ArgumentException("WrapperPackaging.LicenseExpression must not be blank.");
        }
    }

    internal WrapperPackaging Clone() => new WrapperPackaging
    {
        PackageId = PackageId,
        LicenseExpression = LicenseExpression,
        Authors = Authors,
        Description = Description,
        ProjectUrl = ProjectUrl,
        RepositoryUrl = RepositoryUrl,
        Copyright = Copyright,
        Tags = new List<string>(Tags ?? Enumerable.Empty<string>()),
    };
}
