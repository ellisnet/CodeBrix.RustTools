using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Instructions for the person using the consuming application: a title, an explanation, the
/// commands to run in order, and a link to the official documentation. This is data only -
/// CodeBrix.RustTools never runs these commands.
/// </summary>
public sealed class InstallGuidance
{
    /// <summary>What this guidance resolves.</summary>
    public InstallGuidanceTopic Topic { get; init; }

    /// <summary>The operating-system family the commands are written for.</summary>
    public OperatingSystemKind OperatingSystem { get; init; }

    /// <summary>A short heading, for example <c>Install the Rust toolchain with rustup</c>.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>One or more sentences explaining why the step is needed and what it does.</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>The commands to run, in order. Lines starting with <c>#</c> are comments that
    /// separate alternatives (for example one command per Linux distribution family).</summary>
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();

    /// <summary>The official documentation page for this step.</summary>
    public string DocumentationUrl { get; init; } = string.Empty;

    /// <summary>True when at least one command needs administrator rights (sudo or an elevated
    /// prompt).</summary>
    public bool RequiresAdministrator { get; init; }

    /// <summary>True when the user must open a new terminal (or sign out and in) after running
    /// the commands so that PATH changes take effect.</summary>
    public bool RequiresNewShell { get; init; }

    /// <summary>
    /// The guidance as plain text: title, explanation, indented commands and the documentation
    /// link.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Title);
        if (!string.IsNullOrEmpty(Explanation))
        {
            builder.AppendLine(Explanation);
        }

        foreach (var command in Commands)
        {
            builder.Append("    ").AppendLine(command);
        }

        if (!string.IsNullOrEmpty(DocumentationUrl))
        {
            builder.Append("More information: ").AppendLine(DocumentationUrl);
        }

        return builder.ToString().TrimEnd();
    }
}
