using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Reads a thin little-endian 64-bit Mach-O file through its load commands: the cputype, the
/// install name (<c>LC_ID_DYLIB</c>), the libraries it loads (<c>LC_LOAD_DYLIB</c> and its weak,
/// re-export, lazy and upward forms) and the exported symbols. Exports are taken from the
/// symbol table (<c>LC_SYMTAB</c>: external, defined in a section, not private) rather than the
/// dyld export trie; for the libraries cargo links the two agree, but a symbol exported only
/// through the trie (a re-export) is not listed. A universal (fat) file is recognised and its
/// slices are named; it is not read further.
/// </summary>
internal static class MachOFileReader
{
    private const uint Magic64 = 0xFEEDFACF;
    private const uint MhDylib = 6;
    private const uint LcSymtab = 0x2;
    private const uint LcLoadDylib = 0xC;
    private const uint LcIdDylib = 0xD;
    private const uint LcLazyLoadDylib = 0x20;
    private const uint LcLoadWeakDylib = 0x80000018;
    private const uint LcReexportDylib = 0x8000001F;
    private const uint LcLoadUpwardDylib = 0x80000023;

    public static bool IsThin64(BinaryView view) => view.Length >= 32 && view.U32(0) == Magic64;

    public static bool IsUniversal(BinaryView view) => view.Length >= 8 && view.U32BigEndian(0) == MacOSUniversalBinary.FatMagic;

    public static void ReadUniversal(string path, NativeArtifactInfo info)
    {
        info.Format = NativeArtifactFormat.MachOUniversal;
        info.Architecture = "universal";
        var slices = MacOSUniversalBinary.ReadSlices(path);
        info.Is64Bit = true;
        info.Notes = new[] { "Universal file with slices: " + string.Join(", ", slices.Select(s => s.Architecture)) + "; the slices are not read." };
    }

    public static void Read(BinaryView view, NativeArtifactInfo info)
    {
        info.Format = NativeArtifactFormat.MachO;
        info.Is64Bit = true;
        info.Machine = unchecked((int)view.U32(4));
        info.Architecture = ArchitectureOf(info.Machine);
        info.IsSharedLibrary = view.U32(12) == MhDylib;
        var commandCount = view.U32(16);
        var needed = new List<string>();
        var exports = new SortedSet<string>(StringComparer.Ordinal);
        long at = 32;
        for (var i = 0; i < commandCount; i++)
        {
            var command = view.U32(at);
            var size = view.U32(at + 4);
            if (size < 8)
            {
                throw new FormatException($"A Mach-O load command at 0x{at:x} has an invalid size ({size}).");
            }

            switch (command)
            {
                case LcIdDylib:
                    info.LibraryName = view.CString(at + view.U32(at + 8), at + size);
                    break;
                case LcLoadDylib:
                case LcLoadWeakDylib:
                case LcReexportDylib:
                case LcLazyLoadDylib:
                case LcLoadUpwardDylib:
                    needed.Add(view.CString(at + view.U32(at + 8), at + size));
                    break;
                case LcSymtab:
                    ReadSymbols(view, view.U32(at + 8), view.U32(at + 12), view.U32(at + 16), view.U32(at + 20), exports);
                    break;
            }

            at += size;
        }

        info.NeededLibraries = needed;
        info.ExportedSymbols = exports.ToList();
        info.Notes = new[] { "Mach-O exports are read from the symbol table (LC_SYMTAB), not the dyld export trie." };
    }

    /// <summary>The architecture name of a Mach-O cputype.</summary>
    public static string ArchitectureOf(int cpuType) => cpuType switch
    {
        MacOSUniversalBinary.CpuTypeX64 => "x86_64",
        MacOSUniversalBinary.CpuTypeArm64 => "aarch64",
        _ => NativeArtifactInfo.UnknownArchitecture,
    };

    private static void ReadSymbols(BinaryView view, uint symbolOffset, uint count, uint stringOffset, uint stringSize, SortedSet<string> exports)
    {
        for (long i = 0; i < count; i++)
        {
            var entry = symbolOffset + i * 16;
            var nameOffset = view.U32(entry);
            var type = view.U8(entry + 4);
            var stab = (type & 0xE0) != 0;
            var privateExtern = (type & 0x10) != 0;
            var external = (type & 0x01) != 0;
            var definedInSection = (type & 0x0E) == 0x0E;
            if (stab || privateExtern || !external || !definedInSection || nameOffset == 0)
            {
                continue;
            }

            var name = view.CString(stringOffset + nameOffset, (long)stringOffset + stringSize);
            exports.Add(name.StartsWith('_') ? name.Substring(1) : name);
        }
    }
}
