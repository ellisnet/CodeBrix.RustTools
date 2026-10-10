using System;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Archives;

/// <summary>
/// The outcome of <c>RustWorkbench.ExtractAndBuildAsync</c> (both overloads): the extracted
/// source, the project found in it and the build result. Dispose it to delete the extracted
/// files (when <see cref="ExtractedSource.DeleteOnDispose"/> is true) - after running anything
/// you need from <see cref="Build"/>, because the built executables live in the extracted
/// folder.
/// </summary>
public sealed class ExtractAndBuildResult : IDisposable
{
    internal ExtractAndBuildResult(ExtractedSource source, CargoProject project, CargoCommandResult build)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(build);
        Source = source;
        Project = project;
        Build = build;
    }

    /// <summary>The extracted folder and the archive details.</summary>
    public ExtractedSource Source { get; }

    /// <summary>The project opened from <see cref="ExtractedSource.ProjectDirectory"/> (the
    /// workspace root for a workspace).</summary>
    public CargoProject Project { get; }

    /// <summary>The result of <c>cargo build</c> (or of the failed license gate, when
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> was false).</summary>
    public CargoCommandResult Build { get; }

    /// <summary>True when the build succeeded.</summary>
    public bool Success => Build.Success;

    /// <summary>
    /// Disposes <see cref="Source"/>, deleting the extracted folder when
    /// <see cref="ExtractedSource.DeleteOnDispose"/> is true. Safe to call more than once.
    /// </summary>
    public void Dispose() => Source.Dispose();

    /// <inheritdoc />
    public override string ToString() => $"{Project}: {Build}";
}
