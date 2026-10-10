using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// Thrown when a build image or build is requested for a <see cref="CrossTarget"/> that the
/// container builds do not support (see <see cref="CrossTargetInfo.IsSupported"/> and
/// <see cref="CrossTargetInfo.Notes"/>).
/// </summary>
public class CrossTargetNotSupportedException : DockerRustToolsException
{
    /// <summary>
    /// Creates an exception for the given target, with the target's own explanation as the
    /// message.
    /// </summary>
    /// <param name="target">The unsupported target.</param>
    public CrossTargetNotSupportedException(CrossTarget target)
        : this(target, $"Cross-compilation target {target} ({CrossTargetInfo.Get(target).Triple}) is not supported by container builds. {CrossTargetInfo.Get(target).Notes}")
    {
    }

    /// <summary>
    /// Creates an exception for the given target with the supplied message.
    /// </summary>
    /// <param name="target">The unsupported target.</param>
    /// <param name="message">A description of why the target cannot be used.</param>
    public CrossTargetNotSupportedException(CrossTarget target, string message)
        : base(message)
    {
        Target = target;
    }

    /// <summary>The target that was requested.</summary>
    public CrossTarget Target { get; }
}
