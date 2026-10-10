using System;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// Describes an <see cref="OpenSourceLicense"/> member with its SPDX License List data.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SpdxLicenseAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="id">The SPDX short identifier (for example <c>MIT</c>).</param>
    /// <param name="name">The full license name.</param>
    /// <param name="category">The license category.</param>
    /// <param name="osiApproved">True when the Open Source Initiative approved the license.</param>
    /// <param name="fsfFree">True when the Free Software Foundation lists it as a free (libre)
    /// license.</param>
    public SpdxLicenseAttribute(string id, string name, LicenseCategory category, bool osiApproved, bool fsfFree)
    {
        Id = id;
        Name = name;
        Category = category;
        OsiApproved = osiApproved;
        FsfFree = fsfFree;
    }

    /// <summary>The SPDX short identifier.</summary>
    public string Id { get; }

    /// <summary>The full license name.</summary>
    public string Name { get; }

    /// <summary>The license category.</summary>
    public LicenseCategory Category { get; }

    /// <summary>True when the license is OSI approved.</summary>
    public bool OsiApproved { get; }

    /// <summary>True when the FSF lists the license as free (libre).</summary>
    public bool FsfFree { get; }
}
