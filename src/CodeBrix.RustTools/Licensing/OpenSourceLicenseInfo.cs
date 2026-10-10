using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The SPDX data of one <see cref="OpenSourceLicense"/> member, plus static lookups between
/// members and SPDX identifiers (case-insensitive, with deprecated identifiers such as
/// <c>GPL-3.0</c> or <c>GPL-2.0+</c> mapped to their current <c>-only</c> / <c>-or-later</c>
/// forms).
/// </summary>
public sealed class OpenSourceLicenseInfo
{
    private static readonly Lazy<IReadOnlyList<OpenSourceLicenseInfo>> AllInfos = new Lazy<IReadOnlyList<OpenSourceLicenseInfo>>(BuildAll);

    private static readonly Lazy<Dictionary<string, OpenSourceLicense>> ById = new Lazy<Dictionary<string, OpenSourceLicense>>(BuildIndex);

    private static readonly Dictionary<string, OpenSourceLicense> DeprecatedIds = new Dictionary<string, OpenSourceLicense>(StringComparer.OrdinalIgnoreCase)
    {
        ["GPL-2.0"] = OpenSourceLicense.Gpl20Only,
        ["GPL-2.0+"] = OpenSourceLicense.Gpl20OrLater,
        ["GPL-3.0"] = OpenSourceLicense.Gpl30Only,
        ["GPL-3.0+"] = OpenSourceLicense.Gpl30OrLater,
        ["LGPL-2.0"] = OpenSourceLicense.Lgpl20Only,
        ["LGPL-2.0+"] = OpenSourceLicense.Lgpl20OrLater,
        ["LGPL-2.1"] = OpenSourceLicense.Lgpl21Only,
        ["LGPL-2.1+"] = OpenSourceLicense.Lgpl21OrLater,
        ["LGPL-3.0"] = OpenSourceLicense.Lgpl30Only,
        ["LGPL-3.0+"] = OpenSourceLicense.Lgpl30OrLater,
        ["AGPL-3.0"] = OpenSourceLicense.Agpl30Only,
        ["AGPL-3.0+"] = OpenSourceLicense.Agpl30OrLater,
        ["BSD-2-Clause-FreeBSD"] = OpenSourceLicense.Bsd2Clause,
        ["BSD-2-Clause-NetBSD"] = OpenSourceLicense.Bsd2Clause,
    };

    private OpenSourceLicenseInfo(OpenSourceLicense license, SpdxLicenseAttribute attribute)
    {
        License = license;
        SpdxId = attribute.Id;
        Name = attribute.Name;
        Category = attribute.Category;
        IsOsiApproved = attribute.OsiApproved;
        IsFsfFree = attribute.FsfFree;
    }

    /// <summary>The enum member.</summary>
    public OpenSourceLicense License { get; }

    /// <summary>The SPDX short identifier (for example <c>Apache-2.0</c>).</summary>
    public string SpdxId { get; }

    /// <summary>The full license name.</summary>
    public string Name { get; }

    /// <summary>The license category.</summary>
    public LicenseCategory Category { get; }

    /// <summary>True when the license is OSI approved.</summary>
    public bool IsOsiApproved { get; }

    /// <summary>True when the FSF lists the license as free (libre).</summary>
    public bool IsFsfFree { get; }

    /// <summary>True for the GNU family members whose identifiers end in <c>-or-later</c>.</summary>
    public bool IsOrLater => SpdxId.EndsWith("-or-later", StringComparison.Ordinal);

    /// <summary>The data of every <see cref="OpenSourceLicense"/> member, in declaration order.</summary>
    public static IReadOnlyList<OpenSourceLicenseInfo> All => AllInfos.Value;

    /// <summary>
    /// Returns the data of one license.
    /// </summary>
    /// <param name="license">The license.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined member.</exception>
    public static OpenSourceLicenseInfo Get(OpenSourceLicense license)
        => All.FirstOrDefault(i => i.License == license) ?? throw new ArgumentOutOfRangeException(nameof(license), license, "Not a defined license.");

    /// <summary>
    /// Returns the SPDX identifier of a license (for example <c>MIT</c> for
    /// <see cref="OpenSourceLicense.Mit"/>).
    /// </summary>
    /// <param name="license">The license.</param>
    public static string ToSpdxId(OpenSourceLicense license) => Get(license).SpdxId;

    /// <summary>
    /// Looks up a license by SPDX identifier, ignoring case and surrounding white space.
    /// Deprecated identifiers map to their current members (<c>GPL-3.0</c> to
    /// <see cref="OpenSourceLicense.Gpl30Only"/>, <c>GPL-3.0+</c> to
    /// <see cref="OpenSourceLicense.Gpl30OrLater"/>, <c>BSD-2-Clause-FreeBSD</c> to
    /// <see cref="OpenSourceLicense.Bsd2Clause"/>, and so on).
    /// </summary>
    /// <param name="spdxId">The identifier.</param>
    /// <param name="license">The license when found.</param>
    /// <returns><c>true</c> when the identifier is known.</returns>
    public static bool TryParse(string spdxId, out OpenSourceLicense license)
    {
        license = default;
        if (string.IsNullOrWhiteSpace(spdxId))
        {
            return false;
        }

        var id = spdxId.Trim();
        return ById.Value.TryGetValue(id, out license) || DeprecatedIds.TryGetValue(id, out license);
    }

    /// <summary>
    /// Looks up a license by SPDX identifier (see <see cref="TryParse"/>).
    /// </summary>
    /// <param name="spdxId">The identifier.</param>
    /// <exception cref="ArgumentException">The identifier is not known.</exception>
    public static OpenSourceLicense Parse(string spdxId)
        => TryParse(spdxId, out var license) ? license : throw new ArgumentException($"'{spdxId}' is not a known SPDX license identifier.", nameof(spdxId));

    /// <summary>
    /// Returns the members in the given categories.
    /// </summary>
    /// <param name="categories">The categories to include.</param>
    public static IReadOnlyList<OpenSourceLicense> InCategories(params LicenseCategory[] categories)
        => All.Where(i => categories.Contains(i.Category)).Select(i => i.License).ToList();

    /// <inheritdoc />
    public override string ToString() => $"{SpdxId} ({Name})";

    private static IReadOnlyList<OpenSourceLicenseInfo> BuildAll()
    {
        var result = new List<OpenSourceLicenseInfo>();
        foreach (var field in typeof(OpenSourceLicense).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<SpdxLicenseAttribute>();
            if (attribute != null)
            {
                result.Add(new OpenSourceLicenseInfo((OpenSourceLicense)field.GetValue(null), attribute));
            }
        }

        return result;
    }

    private static Dictionary<string, OpenSourceLicense> BuildIndex()
        => All.ToDictionary(i => i.SpdxId, i => i.License, StringComparer.OrdinalIgnoreCase);
}
