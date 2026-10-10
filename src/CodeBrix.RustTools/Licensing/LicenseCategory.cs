namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// A broad grouping of open-source licenses by what they require of software that uses them.
/// </summary>
public enum LicenseCategory
{
    /// <summary>A public-domain dedication or equivalent (no conditions at all).</summary>
    PublicDomain,

    /// <summary>A permissive license (notice / attribution conditions only).</summary>
    Permissive,

    /// <summary>A file- or library-level copyleft license (changes to the licensed files or
    /// library must be shared; the rest of the program is not affected).</summary>
    WeakCopyleft,

    /// <summary>A program-level copyleft license (the combined work must use the same license).</summary>
    StrongCopyleft,

    /// <summary>A copyleft license that also applies to software offered over a network.</summary>
    NetworkCopyleft,

    /// <summary>Anything else (content licenses, unusual terms).</summary>
    Other,
}
