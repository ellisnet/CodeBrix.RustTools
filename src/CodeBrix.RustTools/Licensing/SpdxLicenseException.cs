using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// A license exception from a <c>LICENSE WITH EXCEPTION</c> expression. Exceptions grant extra
/// permissions on top of the license (for example linking without the copyleft applying).
/// </summary>
public sealed class SpdxLicenseException : IEquatable<SpdxLicenseException>
{
    private static readonly (SpdxLicenseExceptionKind Kind, string Id)[] KnownIds =
    {
        (SpdxLicenseExceptionKind.LlvmException, "LLVM-exception"),
        (SpdxLicenseExceptionKind.ClasspathException20, "Classpath-exception-2.0"),
        (SpdxLicenseExceptionKind.GccException31, "GCC-exception-3.1"),
        (SpdxLicenseExceptionKind.BisonException22, "Bison-exception-2.2"),
        (SpdxLicenseExceptionKind.AutoconfException30, "Autoconf-exception-3.0"),
        (SpdxLicenseExceptionKind.OpenJdkAssemblyException10, "OpenJDK-assembly-exception-1.0"),
        (SpdxLicenseExceptionKind.LinuxSyscallNote, "Linux-syscall-note"),
        (SpdxLicenseExceptionKind.MifException, "mif-exception"),
    };

    private SpdxLicenseException(SpdxLicenseExceptionKind kind, string id)
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>The recognised exception, or <see cref="SpdxLicenseExceptionKind.Unknown"/>.</summary>
    public SpdxLicenseExceptionKind Kind { get; }

    /// <summary>The SPDX identifier: the canonical spelling for a known exception, the text as
    /// written for an unknown one.</summary>
    public string Id { get; }

    /// <summary>True when the exception is one of the recognised ones.</summary>
    public bool IsKnown => Kind != SpdxLicenseExceptionKind.Unknown;

    /// <summary>The identifiers of every recognised exception.</summary>
    public static IReadOnlyList<string> KnownExceptionIds => KnownIds.Select(k => k.Id).ToList();

    /// <summary>
    /// Creates the exception object for an identifier (case-insensitive). Unrecognised text
    /// gives an exception of kind <see cref="SpdxLicenseExceptionKind.Unknown"/> that keeps the text.
    /// </summary>
    /// <param name="id">The exception identifier.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is blank.</exception>
    public static SpdxLicenseException Parse(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim();
        foreach (var known in KnownIds)
        {
            if (string.Equals(known.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return new SpdxLicenseException(known.Kind, known.Id);
            }
        }

        return new SpdxLicenseException(SpdxLicenseExceptionKind.Unknown, trimmed);
    }

    /// <summary>
    /// Returns the exception object for a recognised kind.
    /// </summary>
    /// <param name="kind">A known kind.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is
    /// <see cref="SpdxLicenseExceptionKind.Unknown"/> or undefined.</exception>
    public static SpdxLicenseException FromKind(SpdxLicenseExceptionKind kind)
    {
        foreach (var known in KnownIds)
        {
            if (known.Kind == kind)
            {
                return new SpdxLicenseException(known.Kind, known.Id);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only recognised exceptions have a fixed identifier.");
    }

    /// <inheritdoc />
    public bool Equals(SpdxLicenseException other)
        => other != null && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as SpdxLicenseException);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);

    /// <inheritdoc />
    public override string ToString() => Id;
}
