using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>
/// A minimal ELF64 little-endian reader: machine, DT_SONAME and DT_NEEDED from the dynamic
/// section, and the defined (exported) and undefined (imported) global names of the dynamic
/// symbol table. Section headers are
/// required (stripping keeps them).
/// </summary>
internal static class ElfReader
{
    private const uint SectionTypeDynamic = 6;
    private const uint SectionTypeDynamicSymbols = 11;
    private const long TagNeeded = 1;
    private const long TagSoname = 14;

    public static bool IsElf(byte[] data)
        => data.Length >= 4 && data[0] == 0x7F && data[1] == (byte)'E' && data[2] == (byte)'L' && data[3] == (byte)'F';

    public static LinkedBinaryInfo Read(string path, byte[] data)
    {
        if (!IsElf(data) || data.Length < 64)
        {
            throw new FormatException(path + " is not an ELF file.");
        }

        if (data[4] != 2 || data[5] != 1)
        {
            throw new FormatException(path + " is not a 64-bit little-endian ELF file.");
        }

        var machine = BinaryData.U16(data, 18);
        var sectionOffset = (long)BinaryData.U64(data, 0x28);
        var sectionSize = BinaryData.U16(data, 0x3A);
        var sectionCount = BinaryData.U16(data, 0x3C);
        var warnings = new List<string>();
        var needed = new List<string>();
        var exports = new List<string>();
        var imports = new List<string>();
        string soname = null;

        if (sectionOffset == 0 || sectionCount == 0)
        {
            warnings.Add("The ELF file has no section headers; its dynamic section was not read.");
        }

        for (var i = 0; i < sectionCount; i++)
        {
            var header = sectionOffset + ((long)i * sectionSize);
            var type = BinaryData.U32(data, header + 4);
            if (type != SectionTypeDynamic && type != SectionTypeDynamicSymbols)
            {
                continue;
            }

            var offset = (long)BinaryData.U64(data, header + 24);
            var size = (long)BinaryData.U64(data, header + 32);
            var link = BinaryData.U32(data, header + 40);
            var entrySize = (long)BinaryData.U64(data, header + 56);
            var stringsHeader = sectionOffset + ((long)link * sectionSize);
            var stringsOffset = (long)BinaryData.U64(data, stringsHeader + 24);
            var stringsEnd = stringsOffset + (long)BinaryData.U64(data, stringsHeader + 32);

            if (type == SectionTypeDynamic)
            {
                var step = entrySize > 0 ? entrySize : 16;
                for (var entry = offset; entry + 16 <= offset + size; entry += step)
                {
                    var tag = (long)BinaryData.U64(data, entry);
                    if (tag == 0)
                    {
                        break;
                    }

                    if (tag == TagNeeded || tag == TagSoname)
                    {
                        var name = BinaryData.CString(data, stringsOffset + (long)BinaryData.U64(data, entry + 8), stringsEnd);
                        if (tag == TagNeeded)
                        {
                            needed.Add(name);
                        }
                        else
                        {
                            soname = name;
                        }
                    }
                }
            }
            else
            {
                var step = entrySize > 0 ? entrySize : 24;
                for (var entry = offset + step; entry + 24 <= offset + size; entry += step)
                {
                    var sectionIndex = BinaryData.U16(data, entry + 6);
                    var nameOffset = BinaryData.U32(data, entry);
                    var info = data[entry + 4];
                    var other = data[entry + 5];
                    var bind = info >> 4;
                    var symbolType = info & 0xF;
                    var visibility = other & 0x3;
                    var defined = sectionIndex != 0;
                    var global = bind == 1 || bind == 2 || bind == 10;
                    var visible = visibility == 0 || visibility == 3;
                    if (defined && global && visible && symbolType != 3 && symbolType != 4 && nameOffset != 0)
                    {
                        exports.Add(BinaryData.CString(data, stringsOffset + nameOffset, stringsEnd));
                    }
                    else if (!defined && global && nameOffset != 0)
                    {
                        imports.Add(BinaryData.CString(data, stringsOffset + nameOffset, stringsEnd));
                    }
                }
            }
        }

        return new LinkedBinaryInfo
        {
            Path = path,
            Format = LinkedBinaryFormat.Elf,
            Machine = machine switch
            {
                62 => "x86-64",
                183 => "arm64",
                243 => "riscv64",
                _ => machine.ToString(CultureInfo.InvariantCulture),
            },
            OwnName = soname,
            LinkedLibraries = needed,
            ExportedSymbols = exports,
            ImportedSymbols = imports,
            Warnings = warnings,
        };
    }
}
