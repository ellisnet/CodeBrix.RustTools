namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The SPDX license exceptions (the part after <c>WITH</c>) this library recognises.
/// </summary>
public enum SpdxLicenseExceptionKind
{
    /// <summary>An exception identifier that is not in this list (see
    /// <see cref="SpdxLicenseException.Id"/> for the text).</summary>
    Unknown,

    /// <summary><c>LLVM-exception</c> (LLVM Exception, used with Apache-2.0).</summary>
    LlvmException,

    /// <summary><c>Classpath-exception-2.0</c> (GNU Classpath linking exception).</summary>
    ClasspathException20,

    /// <summary><c>GCC-exception-3.1</c> (GCC Runtime Library exception 3.1).</summary>
    GccException31,

    /// <summary><c>Bison-exception-2.2</c> (Bison parser output exception).</summary>
    BisonException22,

    /// <summary><c>Autoconf-exception-3.0</c> (Autoconf generated-script exception).</summary>
    AutoconfException30,

    /// <summary><c>OpenJDK-assembly-exception-1.0</c> (OpenJDK Assembly exception 1.0).</summary>
    OpenJdkAssemblyException10,

    /// <summary><c>Linux-syscall-note</c> (Linux kernel user-space API note).</summary>
    LinuxSyscallNote,

    /// <summary><c>mif-exception</c> (Macros and Inline Functions exception).</summary>
    MifException,
}
