namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// Common open-source licenses, identified by their SPDX License List short identifiers
/// (https://spdx.org/licenses/). Each member carries a <see cref="SpdxLicenseAttribute"/> with
/// the identifier, full name, category and OSI / FSF status; use
/// <see cref="OpenSourceLicenseInfo"/> to look members up by identifier.
/// </summary>
public enum OpenSourceLicense
{
    /// <summary>MIT: MIT License.</summary>
    [SpdxLicense("MIT", "MIT License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Mit,

    /// <summary>MIT-0: MIT No Attribution.</summary>
    [SpdxLicense("MIT-0", "MIT No Attribution", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    Mit0,

    /// <summary>Apache-2.0: Apache License 2.0.</summary>
    [SpdxLicense("Apache-2.0", "Apache License 2.0", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Apache20,

    /// <summary>Apache-1.1: Apache License 1.1.</summary>
    [SpdxLicense("Apache-1.1", "Apache License 1.1", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Apache11,

    /// <summary>BSD-1-Clause: BSD 1-Clause License.</summary>
    [SpdxLicense("BSD-1-Clause", "BSD 1-Clause License", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    Bsd1Clause,

    /// <summary>BSD-2-Clause: BSD 2-Clause &quot;Simplified&quot; License.</summary>
    [SpdxLicense("BSD-2-Clause", "BSD 2-Clause \"Simplified\" License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Bsd2Clause,

    /// <summary>BSD-2-Clause-Patent: BSD-2-Clause Plus Patent License.</summary>
    [SpdxLicense("BSD-2-Clause-Patent", "BSD-2-Clause Plus Patent License", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    Bsd2ClausePatent,

    /// <summary>BSD-3-Clause: BSD 3-Clause &quot;New&quot; or &quot;Revised&quot; License.</summary>
    [SpdxLicense("BSD-3-Clause", "BSD 3-Clause \"New\" or \"Revised\" License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Bsd3Clause,

    /// <summary>BSD-4-Clause: BSD 4-Clause &quot;Original&quot; or &quot;Old&quot; License.</summary>
    [SpdxLicense("BSD-4-Clause", "BSD 4-Clause \"Original\" or \"Old\" License", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    Bsd4Clause,

    /// <summary>0BSD: BSD Zero Clause License.</summary>
    [SpdxLicense("0BSD", "BSD Zero Clause License", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    ZeroBsd,

    /// <summary>ISC: ISC License.</summary>
    [SpdxLicense("ISC", "ISC License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Isc,

    /// <summary>Zlib: zlib License.</summary>
    [SpdxLicense("Zlib", "zlib License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Zlib,

    /// <summary>zlib-acknowledgement: zlib/libpng License with Acknowledgement.</summary>
    [SpdxLicense("zlib-acknowledgement", "zlib/libpng License with Acknowledgement", LicenseCategory.Permissive, osiApproved: false, fsfFree: false)]
    ZlibAcknowledgement,

    /// <summary>BSL-1.0: Boost Software License 1.0.</summary>
    [SpdxLicense("BSL-1.0", "Boost Software License 1.0", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Bsl10,

    /// <summary>Unlicense: The Unlicense.</summary>
    [SpdxLicense("Unlicense", "The Unlicense", LicenseCategory.PublicDomain, osiApproved: true, fsfFree: true)]
    Unlicense,

    /// <summary>CC0-1.0: Creative Commons Zero v1.0 Universal.</summary>
    [SpdxLicense("CC0-1.0", "Creative Commons Zero v1.0 Universal", LicenseCategory.PublicDomain, osiApproved: false, fsfFree: true)]
    Cc010,

    /// <summary>Unicode-3.0: Unicode License v3.</summary>
    [SpdxLicense("Unicode-3.0", "Unicode License v3", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    Unicode30,

    /// <summary>Unicode-DFS-2016: Unicode License Agreement - Data Files and Software (2016).</summary>
    [SpdxLicense("Unicode-DFS-2016", "Unicode License Agreement - Data Files and Software (2016)", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    UnicodeDfs2016,

    /// <summary>CDLA-Permissive-2.0: Community Data License Agreement Permissive 2.0.</summary>
    [SpdxLicense("CDLA-Permissive-2.0", "Community Data License Agreement Permissive 2.0", LicenseCategory.Permissive, osiApproved: false, fsfFree: false)]
    CdlaPermissive20,

    /// <summary>WTFPL: Do What The F*ck You Want To Public License.</summary>
    [SpdxLicense("WTFPL", "Do What The F*ck You Want To Public License", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    Wtfpl,

    /// <summary>BlueOak-1.0.0: Blue Oak Model License 1.0.0.</summary>
    [SpdxLicense("BlueOak-1.0.0", "Blue Oak Model License 1.0.0", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    BlueOak100,

    /// <summary>X11: X11 License.</summary>
    [SpdxLicense("X11", "X11 License", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    X11,

    /// <summary>NCSA: University of Illinois/NCSA Open Source License.</summary>
    [SpdxLicense("NCSA", "University of Illinois/NCSA Open Source License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Ncsa,

    /// <summary>PostgreSQL: PostgreSQL License.</summary>
    [SpdxLicense("PostgreSQL", "PostgreSQL License", LicenseCategory.Permissive, osiApproved: true, fsfFree: false)]
    PostgreSql,

    /// <summary>OpenSSL: OpenSSL License.</summary>
    [SpdxLicense("OpenSSL", "OpenSSL License", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    OpenSsl,

    /// <summary>Python-2.0: Python License 2.0.</summary>
    [SpdxLicense("Python-2.0", "Python License 2.0", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Python20,

    /// <summary>Artistic-2.0: Artistic License 2.0.</summary>
    [SpdxLicense("Artistic-2.0", "Artistic License 2.0", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    Artistic20,

    /// <summary>MS-PL: Microsoft Public License.</summary>
    [SpdxLicense("MS-PL", "Microsoft Public License", LicenseCategory.Permissive, osiApproved: true, fsfFree: true)]
    MsPl,

    /// <summary>Ruby: Ruby License.</summary>
    [SpdxLicense("Ruby", "Ruby License", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    Ruby,

    /// <summary>bzip2-1.0.6: bzip2 and libbzip2 License v1.0.6.</summary>
    [SpdxLicense("bzip2-1.0.6", "bzip2 and libbzip2 License v1.0.6", LicenseCategory.Permissive, osiApproved: false, fsfFree: false)]
    Bzip2106,

    /// <summary>curl: curl License.</summary>
    [SpdxLicense("curl", "curl License", LicenseCategory.Permissive, osiApproved: false, fsfFree: false)]
    Curl,

    /// <summary>CC-BY-4.0: Creative Commons Attribution 4.0 International.</summary>
    [SpdxLicense("CC-BY-4.0", "Creative Commons Attribution 4.0 International", LicenseCategory.Permissive, osiApproved: false, fsfFree: true)]
    CcBy40,

    /// <summary>MPL-2.0: Mozilla Public License 2.0.</summary>
    [SpdxLicense("MPL-2.0", "Mozilla Public License 2.0", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Mpl20,

    /// <summary>MPL-2.0-no-copyleft-exception: Mozilla Public License 2.0 (no copyleft exception).</summary>
    [SpdxLicense("MPL-2.0-no-copyleft-exception", "Mozilla Public License 2.0 (no copyleft exception)", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: false)]
    Mpl20NoCopyleftException,

    /// <summary>MPL-1.1: Mozilla Public License 1.1.</summary>
    [SpdxLicense("MPL-1.1", "Mozilla Public License 1.1", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Mpl11,

    /// <summary>LGPL-2.0-only: GNU Library General Public License v2 only.</summary>
    [SpdxLicense("LGPL-2.0-only", "GNU Library General Public License v2 only", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: false)]
    Lgpl20Only,

    /// <summary>LGPL-2.0-or-later: GNU Library General Public License v2 or later.</summary>
    [SpdxLicense("LGPL-2.0-or-later", "GNU Library General Public License v2 or later", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: false)]
    Lgpl20OrLater,

    /// <summary>LGPL-2.1-only: GNU Lesser General Public License v2.1 only.</summary>
    [SpdxLicense("LGPL-2.1-only", "GNU Lesser General Public License v2.1 only", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Lgpl21Only,

    /// <summary>LGPL-2.1-or-later: GNU Lesser General Public License v2.1 or later.</summary>
    [SpdxLicense("LGPL-2.1-or-later", "GNU Lesser General Public License v2.1 or later", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Lgpl21OrLater,

    /// <summary>LGPL-3.0-only: GNU Lesser General Public License v3.0 only.</summary>
    [SpdxLicense("LGPL-3.0-only", "GNU Lesser General Public License v3.0 only", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Lgpl30Only,

    /// <summary>LGPL-3.0-or-later: GNU Lesser General Public License v3.0 or later.</summary>
    [SpdxLicense("LGPL-3.0-or-later", "GNU Lesser General Public License v3.0 or later", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Lgpl30OrLater,

    /// <summary>EPL-1.0: Eclipse Public License 1.0.</summary>
    [SpdxLicense("EPL-1.0", "Eclipse Public License 1.0", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Epl10,

    /// <summary>EPL-2.0: Eclipse Public License 2.0.</summary>
    [SpdxLicense("EPL-2.0", "Eclipse Public License 2.0", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Epl20,

    /// <summary>CDDL-1.0: Common Development and Distribution License 1.0.</summary>
    [SpdxLicense("CDDL-1.0", "Common Development and Distribution License 1.0", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Cddl10,

    /// <summary>MS-RL: Microsoft Reciprocal License.</summary>
    [SpdxLicense("MS-RL", "Microsoft Reciprocal License", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    MsRl,

    /// <summary>OFL-1.1: SIL Open Font License 1.1.</summary>
    [SpdxLicense("OFL-1.1", "SIL Open Font License 1.1", LicenseCategory.WeakCopyleft, osiApproved: true, fsfFree: true)]
    Ofl11,

    /// <summary>GPL-2.0-only: GNU General Public License v2.0 only.</summary>
    [SpdxLicense("GPL-2.0-only", "GNU General Public License v2.0 only", LicenseCategory.StrongCopyleft, osiApproved: true, fsfFree: true)]
    Gpl20Only,

    /// <summary>GPL-2.0-or-later: GNU General Public License v2.0 or later.</summary>
    [SpdxLicense("GPL-2.0-or-later", "GNU General Public License v2.0 or later", LicenseCategory.StrongCopyleft, osiApproved: true, fsfFree: true)]
    Gpl20OrLater,

    /// <summary>GPL-3.0-only: GNU General Public License v3.0 only.</summary>
    [SpdxLicense("GPL-3.0-only", "GNU General Public License v3.0 only", LicenseCategory.StrongCopyleft, osiApproved: true, fsfFree: true)]
    Gpl30Only,

    /// <summary>GPL-3.0-or-later: GNU General Public License v3.0 or later.</summary>
    [SpdxLicense("GPL-3.0-or-later", "GNU General Public License v3.0 or later", LicenseCategory.StrongCopyleft, osiApproved: true, fsfFree: true)]
    Gpl30OrLater,

    /// <summary>EUPL-1.2: European Union Public License 1.2.</summary>
    [SpdxLicense("EUPL-1.2", "European Union Public License 1.2", LicenseCategory.StrongCopyleft, osiApproved: true, fsfFree: true)]
    Eupl12,

    /// <summary>AGPL-3.0-only: GNU Affero General Public License v3.0 only.</summary>
    [SpdxLicense("AGPL-3.0-only", "GNU Affero General Public License v3.0 only", LicenseCategory.NetworkCopyleft, osiApproved: true, fsfFree: true)]
    Agpl30Only,

    /// <summary>AGPL-3.0-or-later: GNU Affero General Public License v3.0 or later.</summary>
    [SpdxLicense("AGPL-3.0-or-later", "GNU Affero General Public License v3.0 or later", LicenseCategory.NetworkCopyleft, osiApproved: true, fsfFree: true)]
    Agpl30OrLater,

    /// <summary>CC-BY-SA-4.0: Creative Commons Attribution Share Alike 4.0 International.</summary>
    [SpdxLicense("CC-BY-SA-4.0", "Creative Commons Attribution Share Alike 4.0 International", LicenseCategory.Other, osiApproved: false, fsfFree: true)]
    CcBySa40,

    /// <summary>Vim: Vim License.</summary>
    [SpdxLicense("Vim", "Vim License", LicenseCategory.Other, osiApproved: false, fsfFree: true)]
    Vim,
}
