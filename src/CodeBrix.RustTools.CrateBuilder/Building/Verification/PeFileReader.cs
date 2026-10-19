using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Reads a PE / COFF image: the machine, whether it is a DLL, the export directory (the DLL's
/// own name and the exported names) and the import directory (the DLL names it loads).
/// Delay-load imports are not read.
/// </summary>
internal static class PeFileReader
{
    private const ushort ImageFileDll = 0x2000;
    private const ushort Pe32Magic = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;

    public static bool IsPe(BinaryView view)
    {
        if (!view.StartsWith(new byte[] { (byte)'M', (byte)'Z' }) || view.Length < 0x40)
        {
            return false;
        }

        var peOffset = view.U32(0x3C);
        return peOffset + 4 <= view.Length && view.U32(peOffset) == 0x00004550;
    }

    public static void Read(BinaryView view, NativeArtifactInfo info)
    {
        info.Format = NativeArtifactFormat.Pe;
        var notes = new List<string> { "PE delay-load imports are not listed." };
        long coff = view.U32(0x3C) + 4;
        info.Machine = view.U16(coff);
        info.Architecture = ArchitectureOf(info.Machine);
        var sectionCount = view.U16(coff + 2);
        var optionalSize = view.U16(coff + 16);
        var characteristics = view.U16(coff + 18);
        info.IsSharedLibrary = (characteristics & ImageFileDll) != 0;
        var optional = coff + 20;
        var magic = view.U16(optional);
        if (magic != Pe32Magic && magic != Pe32PlusMagic)
        {
            throw new FormatException($"Unknown PE optional header magic 0x{magic:x}.");
        }

        info.Is64Bit = magic == Pe32PlusMagic;
        var directoryCountAt = optional + (info.Is64Bit ? 108 : 92);
        var directoriesAt = optional + (info.Is64Bit ? 112 : 96);
        var directoryCount = view.U32(directoryCountAt);
        var sections = new List<Section>();
        var sectionTable = optional + optionalSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var at = sectionTable + (long)i * 40;
            sections.Add(new Section(view.U32(at + 12), Math.Max(view.U32(at + 8), view.U32(at + 16)), view.U32(at + 20)));
        }

        var exports = new SortedSet<string>(StringComparer.Ordinal);
        if (directoryCount > 0 && view.U32(directoriesAt) != 0)
        {
            var directory = Offset(sections, view.U32(directoriesAt));
            info.LibraryName = view.CString(Offset(sections, view.U32(directory + 12)));
            var nameCount = view.U32(directory + 24);
            var names = Offset(sections, view.U32(directory + 32));
            for (long i = 0; i < nameCount; i++)
            {
                exports.Add(view.CString(Offset(sections, view.U32(names + i * 4))));
            }
        }

        var needed = new List<string>();
        if (directoryCount > 1 && view.U32(directoriesAt + 8) != 0)
        {
            var descriptor = Offset(sections, view.U32(directoriesAt + 8));
            for (var guard = 0; guard < 65536; guard++, descriptor += 20)
            {
                var nameRva = view.U32(descriptor + 12);
                if (nameRva == 0 && view.U32(descriptor) == 0 && view.U32(descriptor + 16) == 0)
                {
                    break;
                }

                if (nameRva != 0)
                {
                    needed.Add(view.CString(Offset(sections, nameRva)));
                }
            }
        }

        info.ExportedSymbols = exports.ToList();
        info.NeededLibraries = needed;
        info.Notes = notes;
    }

    /// <summary>The architecture name of a PE <c>Machine</c> value.</summary>
    public static string ArchitectureOf(int machine) => machine switch
    {
        0x8664 => "x86_64",
        0xAA64 => "aarch64",
        0x14C => "x86",
        0x1C4 => "arm",
        0x5064 => "riscv64",
        _ => NativeArtifactInfo.UnknownArchitecture,
    };

    private static long Offset(List<Section> sections, uint rva)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + (long)section.Size)
            {
                return section.RawOffset + (rva - section.VirtualAddress);
            }
        }

        throw new FormatException($"The PE relative address 0x{rva:x} lies in no section.");
    }

    private sealed record Section(uint VirtualAddress, uint Size, uint RawOffset);
}
