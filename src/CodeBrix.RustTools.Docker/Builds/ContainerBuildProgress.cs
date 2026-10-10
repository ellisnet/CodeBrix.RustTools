using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// One progress report from a container build: the stage, a message, and while cargo runs the
/// cargo progress itself.
/// </summary>
public sealed class ContainerBuildProgress
{
    /// <summary>The stage the build is in.</summary>
    public BuildStage Stage { get; init; }

    /// <summary>A short description of what is happening (for example a line of image-pull
    /// output or <c>Copying 42 files</c>).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The cargo progress report this update carries; set only in the
    /// <see cref="BuildStage.Building"/> stage, otherwise <c>null</c>.
    /// </summary>
    public CargoProgress CargoProgress { get; init; }

    /// <summary>The overall completion in percent (0..100) when it can be estimated; otherwise
    /// <c>null</c>.</summary>
    public double? Percent { get; init; }

    /// <summary>Returns the stage and message (and the cargo progress when present), for
    /// logs.</summary>
    public override string ToString()
        => CargoProgress != null
            ? $"[{Stage}] {CargoProgress}"
            : string.IsNullOrEmpty(Message) ? $"[{Stage}]" : $"[{Stage}] {Message}";
}
