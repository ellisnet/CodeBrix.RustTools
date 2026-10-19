using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>
/// What the WebGPU link verifier needs from a native library: its format and machine, its own
/// name, the libraries it links to and the symbols it exports.
/// </summary>
internal sealed class LinkedBinaryInfo
{
    /// <summary>The file that was read.</summary>
    public string Path { get; init; }

    /// <summary>The container format.</summary>
    public LinkedBinaryFormat Format { get; init; }

    /// <summary>The machine: <c>x86-64</c>, <c>arm64</c>, <c>riscv64</c>, or the raw number as
    /// text for anything else.</summary>
    public string Machine { get; init; }

    /// <summary>The library's own name: the ELF SONAME, the Mach-O LC_ID_DYLIB install name,
    /// or the PE export directory's DLL name; <c>null</c> when it has none.</summary>
    public string OwnName { get; init; }

    /// <summary>The libraries it links to, in file order: ELF DT_NEEDED entries, Mach-O
    /// LC_LOAD_DYLIB / LC_LOAD_WEAK_DYLIB / LC_REEXPORT_DYLIB / LC_LAZY_LOAD_DYLIB /
    /// LC_LOAD_UPWARD_DYLIB names, PE import and delay-import DLL names.</summary>
    public IReadOnlyList<string> LinkedLibraries { get; init; } = Array.Empty<string>();

    /// <summary>The names of the symbols it exports (defined global symbols of the ELF dynamic
    /// symbol table, external defined symbols of the Mach-O symbol table without the leading
    /// underscore, PE export names).</summary>
    public IReadOnlyList<string> ExportedSymbols { get; init; } = Array.Empty<string>();

    /// <summary>The names of the symbols it needs from other libraries: undefined global
    /// symbols of the ELF dynamic symbol table, external undefined symbols of the Mach-O symbol
    /// table (without the leading underscore). Not read for PE files (a PE file names the DLL
    /// of every import, see <see cref="LinkedLibraries"/>).</summary>
    public IReadOnlyList<string> ImportedSymbols { get; init; } = Array.Empty<string>();

    /// <summary>What could not be read (for example an ELF file without section
    /// headers).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
