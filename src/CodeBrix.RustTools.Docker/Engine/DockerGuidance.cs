using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// Instructions for the person using the consuming application: a title, an explanation, the
/// commands to run in order and a link to the official documentation. This is data only -
/// CodeBrix.RustTools.Docker never runs these commands.
/// </summary>
public sealed class DockerGuidance
{
    /// <summary>What this guidance resolves.</summary>
    public DockerGuidanceTopic Topic { get; init; }

    /// <summary>The operating-system family the commands are written for.</summary>
    public OperatingSystemKind OperatingSystem { get; init; }

    /// <summary>A short heading, for example <c>Install Docker Desktop</c>.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>One or more sentences explaining why the step is needed and what it does.</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>The commands to run, in order. Lines starting with <c>#</c> are comments (for
    /// example a step that is done in a user interface rather than a terminal).</summary>
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();

    /// <summary>The official documentation page for this step.</summary>
    public string DocumentationUrl { get; init; } = string.Empty;

    /// <summary>True when at least one command needs administrator rights (sudo or an elevated
    /// prompt).</summary>
    public bool RequiresAdministrator { get; init; }

    /// <summary>True when the user must sign out and back in (or open a new terminal) before the
    /// change takes effect.</summary>
    public bool RequiresNewSession { get; init; }

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
