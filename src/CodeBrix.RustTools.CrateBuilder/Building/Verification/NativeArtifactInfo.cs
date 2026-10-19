using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// What <see cref="NativeArtifactReader"/> found in a native file: its format and
/// architecture, its own library name, the libraries it loads and the symbols it exports.
/// </summary>
internal sealed class NativeArtifactInfo
{
    /// <summary>The architecture name used for an architecture the reader does not
    /// know.</summary>
    public const string UnknownArchitecture = "unknown";

    /// <summary>The file that was read (absolute).</summary>
    public string Path { get; set; }

    /// <summary>The container format.</summary>
    public NativeArtifactFormat Format { get; set; }

    /// <summary>The CPU architecture: <c>x86_64</c>, <c>aarch64</c>, <c>riscv64</c>,
    /// <c>x86</c>, <c>arm</c>, <c>universal</c> (a fat Mach-O) or
    /// <see cref="UnknownArchitecture"/>.</summary>
    public string Architecture { get; set; } = UnknownArchitecture;

    /// <summary>The raw machine value: ELF <c>e_machine</c>, PE <c>Machine</c> or Mach-O
    /// <c>cputype</c>.</summary>
    public int Machine { get; set; }

    /// <summary>Whether the file is a 64-bit image.</summary>
    public bool Is64Bit { get; set; }

    /// <summary>Whether the file is a shared library (ELF <c>ET_DYN</c>, a PE DLL, a Mach-O
    /// <c>MH_DYLIB</c>).</summary>
    public bool IsSharedLibrary { get; set; }

    /// <summary>The library's own name: the ELF <c>SONAME</c>, the Mach-O install name
    /// (<c>LC_ID_DYLIB</c>) or the PE export directory's DLL name; <c>null</c> when the file
    /// has none.</summary>
    public string LibraryName { get; set; }

    /// <summary>The libraries the file loads, in file order: ELF <c>DT_NEEDED</c>, Mach-O
    /// <c>LC_LOAD_DYLIB</c> (and the weak, re-export, lazy and upward forms) or the PE import
    /// table's DLL names.</summary>
    public IReadOnlyList<string> NeededLibraries { get; set; } = Array.Empty<string>();

    /// <summary>The exported symbol names, sorted ordinally and without repeats: ELF dynamic
    /// symbols that are defined, global or weak and not hidden; PE export table names; Mach-O
    /// external defined symbols of <c>LC_SYMTAB</c> with the leading underscore of the C name
    /// removed.</summary>
    public IReadOnlyList<string> ExportedSymbols { get; set; } = Array.Empty<string>();

    /// <summary>ELF only: the symbol versions the file needs from other libraries
    /// (<c>.gnu.version_r</c>), for example <c>GLIBC_2.28</c>, sorted ordinally.</summary>
    public IReadOnlyList<string> RequiredSymbolVersions { get; set; } = Array.Empty<string>();

    /// <summary>ELF only: the Android API level the library was linked for (the NDK's
    /// <c>.note.android.ident</c> note), or <c>null</c> when the file has no such note (every
    /// file that is not an Android library).</summary>
    public int? AndroidApiLevel { get; set; }

    /// <summary>What the reader could not determine, or how it read something (for example
    /// that Mach-O exports come from the symbol table, not the dyld export trie).</summary>
    public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();

    /// <summary>"format architecture [shared library] name".</summary>
    public override string ToString()
        => Format + " " + Architecture + (IsSharedLibrary ? " shared library" : string.Empty) + (LibraryName == null ? string.Empty : " " + LibraryName);
}
