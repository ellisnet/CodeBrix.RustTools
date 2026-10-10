namespace CodeBrix.RustTools.Internal;

/// <summary>
/// The spellings of a target triple that cargo's per-target environment variables use.
/// </summary>
internal static class TargetTripleNames
{
    /// <summary>The upper-snake form, <c>AARCH64_LINUX_ANDROID</c>, used in
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c> and friends.</summary>
    public static string Upper(string triple) => (triple ?? string.Empty).Trim().Replace('-', '_').ToUpperInvariant();

    /// <summary>The lower-snake form, <c>aarch64_linux_android</c>, used by the <c>cc</c> crate's
    /// <c>CC_&lt;triple&gt;</c> variables.</summary>
    public static string Lower(string triple) => (triple ?? string.Empty).Trim().Replace('-', '_').ToLowerInvariant();
}
