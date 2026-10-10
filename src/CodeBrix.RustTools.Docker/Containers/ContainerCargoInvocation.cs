using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Everything needed to run one cargo command inside a build container: the command line
/// (cargo under <c>setsid</c>), the environment (with a per-run marker variable that lets a
/// cancelled run be found and killed), and the working directory.
/// </summary>
internal sealed class ContainerCargoInvocation
{
    /// <summary>The cargo subcommand.</summary>
    public CargoCommand Command { get; init; }

    /// <summary>The target the command compiles for.</summary>
    public CrossTarget Target { get; init; }

    /// <summary>The full command run in the container: <c>setsid -w cargo ...</c>.</summary>
    public IReadOnlyList<string> CommandLine { get; init; } = Array.Empty<string>();

    /// <summary>The cargo arguments (without <c>setsid</c> and <c>cargo</c>).</summary>
    public IReadOnlyList<string> CargoArguments { get; init; } = Array.Empty<string>();

    /// <summary>The environment, as <c>NAME=value</c> entries, in the order they are
    /// applied.</summary>
    public IReadOnlyList<string> Environment { get; init; } = Array.Empty<string>();

    /// <summary>The container folder cargo runs in.</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>The <c>NAME=value</c> entry of the marker variable that every process of this
    /// run inherits.</summary>
    public string MarkerEntry { get; init; } = string.Empty;

    /// <summary>The display form of the cargo command line, for results and logs.</summary>
    public string DisplayCommandLine => "cargo " + string.Join(" ", CargoArguments);
}
