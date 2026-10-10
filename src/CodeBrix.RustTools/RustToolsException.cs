using System;

namespace CodeBrix.RustTools;

/// <summary>
/// The base type of every exception that CodeBrix.RustTools throws on purpose. Catch this type
/// to handle any toolchain, cargo, license-policy or archive failure raised by the library.
/// </summary>
public class RustToolsException : Exception
{
    /// <summary>
    /// Creates an exception with a generic message.
    /// </summary>
    public RustToolsException()
        : base("A CodeBrix.RustTools operation failed.")
    {
    }

    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public RustToolsException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one (for example an
    /// <see cref="System.IO.IOException"/> or a TOML parse error).</param>
    public RustToolsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
