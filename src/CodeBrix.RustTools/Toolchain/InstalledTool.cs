namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// What detection found for one tool: whether it is usable, where it is, and which version it
/// reported.
/// </summary>
public sealed class InstalledTool
{
    /// <summary>The tool this entry describes.</summary>
    public RustTool Tool { get; init; }

    /// <summary>The detection outcome. Detection itself only reports
    /// <see cref="ToolStatus.Installed"/>, <see cref="ToolStatus.Missing"/> or
    /// <see cref="ToolStatus.Unknown"/>; <see cref="ToolStatus.Outdated"/> comes from readiness
    /// and update checks.</summary>
    public ToolStatus Status { get; init; } = ToolStatus.Unknown;

    /// <summary>The full path of the executable that was run (for the MSVC tools found through
    /// the Visual Studio installer, the Visual Studio installation folder), or <c>null</c>.</summary>
    public string ExecutablePath { get; init; }

    /// <summary>The parsed version, or <c>null</c> when the tool is missing or printed no
    /// recognisable version.</summary>
    public ToolVersion Version { get; init; }

    /// <summary>The first line of the tool's version output, or <c>null</c>.</summary>
    public string RawVersionOutput { get; init; }

    /// <summary>A short explanation of the status (why the tool is missing or unknown), or
    /// <c>null</c> when the tool is simply installed.</summary>
    public string Message { get; init; }

    /// <summary>True when the tool can be used: <see cref="Status"/> is
    /// <see cref="ToolStatus.Installed"/> or <see cref="ToolStatus.Outdated"/>.</summary>
    public bool IsAvailable => Status == ToolStatus.Installed || Status == ToolStatus.Outdated;

    /// <summary>
    /// Creates a <see cref="ToolStatus.Missing"/> entry with an explanation.
    /// </summary>
    /// <param name="tool">The tool that was not found.</param>
    /// <param name="message">Why it is considered missing.</param>
    public static InstalledTool Missing(RustTool tool, string message)
        => new InstalledTool { Tool = tool, Status = ToolStatus.Missing, Message = message };

    /// <summary>
    /// Creates a <see cref="ToolStatus.Unknown"/> entry with an explanation.
    /// </summary>
    /// <param name="tool">The tool that could not be probed.</param>
    /// <param name="message">Why the probe was inconclusive.</param>
    /// <param name="executablePath">The executable that was found, if any.</param>
    public static InstalledTool Unknown(RustTool tool, string message, string executablePath = null)
        => new InstalledTool { Tool = tool, Status = ToolStatus.Unknown, Message = message, ExecutablePath = executablePath };

    /// <summary>
    /// A one-line description such as <c>Cargo: Installed 1.94.1 (/home/me/.cargo/bin/cargo)</c>.
    /// </summary>
    public override string ToString()
    {
        var text = $"{Tool}: {Status}";
        if (Version != null)
        {
            text += " " + Version;
        }

        if (!string.IsNullOrEmpty(ExecutablePath))
        {
            text += " (" + ExecutablePath + ")";
        }

        if (!string.IsNullOrEmpty(Message))
        {
            text += " - " + Message;
        }

        return text;
    }
}
