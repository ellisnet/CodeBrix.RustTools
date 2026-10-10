namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The detection outcome for one tool.
/// </summary>
public enum ToolStatus
{
    /// <summary>The tool was found and reported a version.</summary>
    Installed,

    /// <summary>The tool was not found, or reported that it is not installed for the active
    /// toolchain.</summary>
    Missing,

    /// <summary>The tool is installed but older than required or than the latest release.</summary>
    Outdated,

    /// <summary>The tool could not be probed reliably (it timed out, failed in an unexpected way,
    /// or the platform has no dependable probe); the library does not guess.</summary>
    Unknown,
}
