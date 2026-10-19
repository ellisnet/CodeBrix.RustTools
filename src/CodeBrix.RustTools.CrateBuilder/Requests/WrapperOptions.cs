using System;
using System.IO;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Asks <see cref="CrateBuilder"/> to write a .NET 10 class library around the built crate.
/// Without it a build produces the native files and the build record only.
/// </summary>
public sealed class WrapperOptions
{
    /// <summary>The wrapper's assembly (and project) name, for example
    /// <c>MyCompany.Geometry</c> (REQUIRED).</summary>
    public string AssemblyName { get; set; }

    /// <summary>The wrapper's root namespace, a dotted C# identifier (REQUIRED).</summary>
    public string RootNamespace { get; set; }

    /// <summary>NuGet metadata; <c>null</c> = a plain class library that is not
    /// packable.</summary>
    public WrapperPackaging Packaging { get; set; }

    /// <summary>What to wrap and how; <c>null</c> = everything that can be mapped.</summary>
    public WrapPlan Plan { get; set; }

    /// <summary>The folder of the wrapper project; <c>null</c> = a folder named
    /// <see cref="AssemblyName"/> beside the request's output folder.</summary>
    public string OutputDirectory { get; set; }

    /// <summary>Also generate a smoke test per wrapped function. Default false.</summary>
    public bool GenerateSmokeTests { get; set; }

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentException">A required setting is missing, the assembly name
    /// is not a valid file name, the root namespace is not a dotted C# identifier, or the
    /// packaging settings are invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AssemblyName))
        {
            throw new ArgumentException("WrapperOptions.AssemblyName is required.");
        }

        var name = AssemblyName.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ' ' }) >= 0 || name.StartsWith('.') || name.EndsWith('.'))
        {
            throw new ArgumentException($"WrapperOptions.AssemblyName '{AssemblyName}' is not a valid assembly name (no spaces, path or wildcard characters, no leading or trailing '.').");
        }

        if (string.IsNullOrWhiteSpace(RootNamespace))
        {
            throw new ArgumentException("WrapperOptions.RootNamespace is required.");
        }

        if (!IsDottedIdentifier(RootNamespace.Trim()))
        {
            throw new ArgumentException($"WrapperOptions.RootNamespace '{RootNamespace}' is not a dotted C# identifier (for example MyCompany.Geometry).");
        }

        Packaging?.Validate();
    }

    internal WrapperOptions Clone() => new WrapperOptions
    {
        AssemblyName = AssemblyName,
        RootNamespace = RootNamespace,
        Packaging = Packaging?.Clone(),
        Plan = Plan?.Clone(),
        OutputDirectory = OutputDirectory,
        GenerateSmokeTests = GenerateSmokeTests,
    };

    internal static bool IsDottedIdentifier(string text)
        => text.Split('.').All(part => part.Length > 0
            && (char.IsLetter(part[0]) || part[0] == '_')
            && part.All(c => char.IsLetterOrDigit(c) || c == '_'));
}
