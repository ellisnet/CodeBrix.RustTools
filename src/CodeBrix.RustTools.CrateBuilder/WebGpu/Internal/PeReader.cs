using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>
/// A minimal PE / COFF reader: machine, the export directory's DLL name and names, and the
/// DLL names of the import and delay-import directories.
/// </summary>
internal static class PeReader
{
    public static bool IsPe(byte[] data) => data.Length >= 0x40 && data[0] == (byte)'M' && data[1] == (byte)'Z';

    public static LinkedBinaryInfo Read(string path, byte[] data)
    {
        if (!IsPe(data))
        {
            throw new FormatException(path + " is not a PE file.");
        }

        var pe = (long)BinaryData.U32(data, 0x3C);
        if (BinaryData.U32(data, pe) != 0x00004550)
        {
            throw new FormatException(path + " has no PE signature.");
        }

        var machine = BinaryData.U16(data, pe + 4);
        var sectionCount = BinaryData.U16(data, pe + 6);
        var optionalSize = BinaryData.U16(data, pe + 20);
        var optional = pe + 24;
        var magic = BinaryData.U16(data, optional);
        var directoryCountOffset = magic == 0x20B ? optional + 108 : optional + 92;
        var directories = magic == 0x20B ? optional + 112 : optional + 96;
        var directoryCount = BinaryData.U32(data, directoryCountOffset);
        var sections = new List<(uint Address, uint Size, uint Raw, uint RawSize)>();
        var sectionTable = optional + optionalSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var header = sectionTable + ((long)i * 40);
            sections.Add((BinaryData.U32(data, header + 12), BinaryData.U32(data, header + 8), BinaryData.U32(data, header + 20), BinaryData.U32(data, header + 16)));
        }

        long FileOffset(uint rva)
        {
            foreach (var section in sections)
            {
                var span = Math.Max(section.Size, section.RawSize);
                if (rva >= section.Address && rva < section.Address + span)
                {
                    return section.Raw + (long)(rva - section.Address);
                }
            }

            throw new FormatException(path + ": RVA 0x" + rva.ToString("X", CultureInfo.InvariantCulture) + " is in no section.");
        }

        (uint Rva, uint Size) Directory(int index)
            => index < directoryCount ? (BinaryData.U32(data, directories + (index * 8)), BinaryData.U32(data, directories + (index * 8) + 4)) : (0u, 0u);

        string ownName = null;
        var exports = new List<string>();
        var export = Directory(0);
        if (export.Rva != 0)
        {
            var table = FileOffset(export.Rva);
            var nameRva = BinaryData.U32(data, table + 12);
            if (nameRva != 0)
            {
                ownName = BinaryData.CString(data, FileOffset(nameRva));
            }

            var count = BinaryData.U32(data, table + 24);
            var names = FileOffset(BinaryData.U32(data, table + 32));
            for (var i = 0; i < count; i++)
            {
                exports.Add(BinaryData.CString(data, FileOffset(BinaryData.U32(data, names + (i * 4L)))));
            }
        }

        var imports = new List<string>();
        var import = Directory(1);
        if (import.Rva != 0)
        {
            for (var descriptor = FileOffset(import.Rva); ; descriptor += 20)
            {
                var lookup = BinaryData.U32(data, descriptor);
                var nameRva = BinaryData.U32(data, descriptor + 12);
                var thunks = BinaryData.U32(data, descriptor + 16);
                if (nameRva == 0 && lookup == 0 && thunks == 0)
                {
                    break;
                }

                if (nameRva != 0)
                {
                    imports.Add(BinaryData.CString(data, FileOffset(nameRva)));
                }
            }
        }

        var delay = Directory(13);
        if (delay.Rva != 0)
        {
            for (var descriptor = FileOffset(delay.Rva); ; descriptor += 32)
            {
                var nameRva = BinaryData.U32(data, descriptor + 4);
                if (nameRva == 0)
                {
                    break;
                }

                imports.Add(BinaryData.CString(data, FileOffset(nameRva)));
            }
        }

        return new LinkedBinaryInfo
        {
            Path = path,
            Format = LinkedBinaryFormat.Pe,
            Machine = machine switch
            {
                0x8664 => "x86-64",
                0xAA64 => "arm64",
                _ => "0x" + machine.ToString("X4", CultureInfo.InvariantCulture),
            },
            OwnName = ownName,
            LinkedLibraries = imports,
            ExportedSymbols = exports,
        };
    }
}
