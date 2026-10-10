using System;
using CodeBrix.RustTools.Licensing;

namespace CodeBrix.RustTools;

/// <summary>
/// Thrown when a license audit finds crates that the configured license policy does not allow
/// and the caller asked for a violation to be raised as an exception.
/// </summary>
public class LicensePolicyViolationException : RustToolsException
{
    /// <summary>
    /// Creates an exception whose <see cref="Summary"/> is the supplied message.
    /// </summary>
    /// <param name="message">A description of the violation; also used as the summary.</param>
    public LicensePolicyViolationException(string message)
        : this(message, message)
    {
    }

    /// <summary>
    /// Creates an exception with a short message and a longer, human-readable summary.
    /// </summary>
    /// <param name="message">A one-line description of the violation.</param>
    /// <param name="summary">A multi-line text summary of the rejected and unresolved crates.</param>
    public LicensePolicyViolationException(string message, string summary)
        : base(message)
    {
        Summary = summary ?? message;
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of the violation; also used as the summary.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public LicensePolicyViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Summary = message;
    }

    /// <summary>
    /// Creates an exception that carries the failed audit report; <see cref="Summary"/> is the
    /// report's <see cref="LicenseAuditReport.ToSummaryText"/>.
    /// </summary>
    /// <param name="message">A one-line description of the violation.</param>
    /// <param name="report">The audit report that failed.</param>
    public LicensePolicyViolationException(string message, LicenseAuditReport report)
        : base(message)
    {
        Report = report;
        Summary = report?.ToSummaryText() ?? message;
    }

    /// <summary>
    /// A human-readable, multi-line summary of the crates that failed the license policy.
    /// </summary>
    public string Summary { get; }

    /// <summary>
    /// The audit report that failed, or <c>null</c> when the exception was created without one.
    /// </summary>
    public LicenseAuditReport Report { get; }
}
