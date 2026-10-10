using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// An allow-list of licenses (and license exceptions) for a license audit, plus rules for
/// crates whose license cannot be decided, workspace members, and per-crate overrides.
/// Start from <see cref="Permissive"/> or <see cref="PermissiveAndWeakCopyleft"/>, or from an
/// empty policy, and add to it with <see cref="Allow"/>.
/// </summary>
public sealed class LicensePolicy
{
    private static readonly Dictionary<OpenSourceLicense, OpenSourceLicense[]> LaterVersions = new Dictionary<OpenSourceLicense, OpenSourceLicense[]>
    {
        [OpenSourceLicense.Gpl20OrLater] = new[] { OpenSourceLicense.Gpl30Only, OpenSourceLicense.Gpl30OrLater },
        [OpenSourceLicense.Lgpl20OrLater] = new[] { OpenSourceLicense.Lgpl21Only, OpenSourceLicense.Lgpl21OrLater, OpenSourceLicense.Lgpl30Only, OpenSourceLicense.Lgpl30OrLater },
        [OpenSourceLicense.Lgpl21OrLater] = new[] { OpenSourceLicense.Lgpl30Only, OpenSourceLicense.Lgpl30OrLater },
    };

    /// <summary>The licenses the policy allows.</summary>
    public ISet<OpenSourceLicense> AllowedLicenses { get; } = new HashSet<OpenSourceLicense>();

    /// <summary>License exception identifiers (case-insensitive) that make an otherwise
    /// disallowed license acceptable when used with <c>WITH</c> (for example
    /// <c>GCC-exception-3.1</c>). An exception attached to an allowed license never causes a
    /// rejection, because exceptions only add permissions.</summary>
    public ISet<string> AllowedExceptions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What happens to crates whose license cannot be decided; default
    /// <see cref="Licensing.UnresolvedLicenseHandling.Reject"/>.</summary>
    public UnresolvedLicenseHandling UnresolvedLicenseHandling { get; set; } = UnresolvedLicenseHandling.Reject;

    /// <summary>When <c>true</c> (the default) the project's own workspace members are not
    /// checked; their findings are <see cref="CrateLicenseStatus.Exempt"/>.</summary>
    public bool ExemptWorkspaceMembers { get; set; } = true;

    /// <summary>Crates (by name, case-insensitive) that are accepted whatever their license,
    /// mapped to the reason (recorded in the finding). Use for crates you have reviewed by hand,
    /// such as a crate that ships only a license file.</summary>
    public IDictionary<string, string> CrateOverrides { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A policy that allows the common permissive and public-domain licenses: MIT, MIT-0,
    /// Apache-2.0, BSD-2-Clause, BSD-3-Clause, 0BSD, ISC, Zlib, BSL-1.0, Unicode-3.0,
    /// Unicode-DFS-2016, Unlicense and CC0-1.0.
    /// </summary>
    public static LicensePolicy Permissive() => new LicensePolicy().Allow(
        OpenSourceLicense.Mit,
        OpenSourceLicense.Mit0,
        OpenSourceLicense.Apache20,
        OpenSourceLicense.Bsd2Clause,
        OpenSourceLicense.Bsd3Clause,
        OpenSourceLicense.ZeroBsd,
        OpenSourceLicense.Isc,
        OpenSourceLicense.Zlib,
        OpenSourceLicense.Bsl10,
        OpenSourceLicense.Unicode30,
        OpenSourceLicense.UnicodeDfs2016,
        OpenSourceLicense.Unlicense,
        OpenSourceLicense.Cc010);

    /// <summary>
    /// <see cref="Permissive"/> plus the weak-copyleft licenses most often found in Rust
    /// dependency graphs: MPL-2.0 and every LGPL version.
    /// </summary>
    public static LicensePolicy PermissiveAndWeakCopyleft() => Permissive().Allow(
        OpenSourceLicense.Mpl20,
        OpenSourceLicense.Lgpl20Only,
        OpenSourceLicense.Lgpl20OrLater,
        OpenSourceLicense.Lgpl21Only,
        OpenSourceLicense.Lgpl21OrLater,
        OpenSourceLicense.Lgpl30Only,
        OpenSourceLicense.Lgpl30OrLater);

    /// <summary>
    /// Adds licenses to <see cref="AllowedLicenses"/> and returns this policy.
    /// </summary>
    /// <param name="licenses">The licenses to allow.</param>
    public LicensePolicy Allow(params OpenSourceLicense[] licenses)
    {
        ArgumentNullException.ThrowIfNull(licenses);
        foreach (var license in licenses)
        {
            AllowedLicenses.Add(license);
        }

        return this;
    }

    /// <summary>
    /// Adds license exception identifiers to <see cref="AllowedExceptions"/> and returns this
    /// policy.
    /// </summary>
    /// <param name="exceptionIds">The exception identifiers (for example
    /// <c>Classpath-exception-2.0</c>).</param>
    public LicensePolicy AllowException(params string[] exceptionIds)
    {
        ArgumentNullException.ThrowIfNull(exceptionIds);
        foreach (var id in exceptionIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                AllowedExceptions.Add(id.Trim());
            }
        }

        return this;
    }

    /// <summary>
    /// Accepts a crate whatever its license (adds to <see cref="CrateOverrides"/>) and returns
    /// this policy.
    /// </summary>
    /// <param name="crateName">The crate name.</param>
    /// <param name="reason">Why the crate is accepted; shown in the audit finding.</param>
    public LicensePolicy Override(string crateName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crateName);
        CrateOverrides[crateName.Trim()] = string.IsNullOrWhiteSpace(reason) ? "accepted by a crate override" : reason;
        return this;
    }

    /// <summary>
    /// True when the policy allows <paramref name="license"/>. An <c>-or-later</c> GNU license
    /// is also allowed when a later version it can be used under is allowed (for example
    /// GPL-2.0-or-later when GPL-3.0-only is allowed).
    /// </summary>
    /// <param name="license">The license.</param>
    public bool IsAllowed(OpenSourceLicense license)
        => AllowedLicenses.Contains(license)
            || (LaterVersions.TryGetValue(license, out var later) && later.Any(AllowedLicenses.Contains));

    /// <summary>
    /// True when the exception identifier is in <see cref="AllowedExceptions"/>.
    /// </summary>
    /// <param name="exception">The exception, or <c>null</c>.</param>
    public bool IsExceptionAllowed(SpdxLicenseException exception) => exception != null && AllowedExceptions.Contains(exception.Id);

    /// <summary>
    /// Returns a copy of this policy that can be changed independently.
    /// </summary>
    public LicensePolicy Clone()
    {
        var copy = new LicensePolicy
        {
            UnresolvedLicenseHandling = UnresolvedLicenseHandling,
            ExemptWorkspaceMembers = ExemptWorkspaceMembers,
        };
        copy.Allow(AllowedLicenses.ToArray());
        copy.AllowException(AllowedExceptions.ToArray());
        foreach (var pair in CrateOverrides)
        {
            copy.CrateOverrides[pair.Key] = pair.Value;
        }

        return copy;
    }

    /// <inheritdoc />
    public override string ToString()
        => "Allowed: " + string.Join(", ", AllowedLicenses.Select(OpenSourceLicenseInfo.ToSpdxId).OrderBy(i => i, StringComparer.OrdinalIgnoreCase))
            + $"; unresolved: {UnresolvedLicenseHandling}; workspace members exempt: {ExemptWorkspaceMembers}";
}
