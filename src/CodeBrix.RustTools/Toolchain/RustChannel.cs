namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The Rust release channel a tool version or toolchain belongs to.
/// </summary>
public enum RustChannel
{
    /// <summary>The channel could not be determined.</summary>
    Unknown,

    /// <summary>A stable release (including pinned versions such as <c>1.94.1</c>).</summary>
    Stable,

    /// <summary>A beta release.</summary>
    Beta,

    /// <summary>A nightly build.</summary>
    Nightly,

    /// <summary>A locally built development compiler.</summary>
    Dev,
}
