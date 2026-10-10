namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The host operating-system families the library gives install guidance for.
/// </summary>
public enum OperatingSystemKind
{
    /// <summary>The operating system could not be identified.</summary>
    Unknown,

    /// <summary>Microsoft Windows.</summary>
    Windows,

    /// <summary>Apple macOS.</summary>
    MacOS,

    /// <summary>Debian or a Debian derivative (Ubuntu, Linux Mint, Raspberry Pi OS, ...),
    /// identified through <c>/etc/os-release</c>; apt commands apply.</summary>
    DebianLinux,

    /// <summary>Any other Linux distribution.</summary>
    OtherLinux,
}
