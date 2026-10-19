using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>
/// A minimal 64-bit little-endian Mach-O reader (thin files, or one slice of a universal
/// file): cputype, LC_ID_DYLIB, the dylib load commands and the external defined symbols of
/// LC_SYMTAB (the dyld export trie is not read; for linker output the two name the same
/// exports).
/// </summary>
internal static class MachOReader
{
    private const uint Magic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint CommandSymtab = 0x2;
    private const uint CommandLoadDylib = 0xC;
    private const uint CommandIdDylib = 0xD;
    private const uint CommandLazyLoadDylib = 0x20;
    private const uint CommandLoadWeakDylib = 0x80000018;
    private const uint CommandReexportDylib = 0x8000001F;
    private const uint CommandLoadUpwardDylib = 0x80000023;
    private const int CpuTypeX64 = 0x01000007;
    private const int CpuTypeArm64 = 0x0100000C;

    public static bool IsMachO(byte[] data)
        => data.Length >= 4 && (BinaryData.U32(data, 0) == Magic64 || BinaryData.U32BigEndian(data, 0) == FatMagic);

    /// <summary>Reads a thin file, or the slice of a universal file whose machine is
    /// <paramref name="preferredMachine"/> (else its first slice).</summary>
    public static LinkedBinaryInfo Read(string path, byte[] data, string preferredMachine = null)
    {
        if (data.Length >= 8 && BinaryData.U32BigEndian(data, 0) == FatMagic)
        {
            var count = BinaryData.U32BigEndian(data, 4);
            LinkedBinaryInfo first = null;
            for (var i = 0; i < count; i++)
            {
                var entry = 8 + (i * 20L);
                var offset = BinaryData.U32BigEndian(data, entry + 8);
                var size = BinaryData.U32BigEndian(data, entry + 12);
                if ((long)offset + size > data.Length)
                {
                    throw new FormatException(path + ": a universal slice is outside the file.");
                }

                var slice = new byte[size];
                Array.Copy(data, offset, slice, 0, size);
                var info = ReadThin(path, slice);
                first ??= info;
                if (preferredMachine == null || string.Equals(info.Machine, preferredMachine, StringComparison.Ordinal))
                {
                    return info;
                }
            }

            return first ?? throw new FormatException(path + " is a universal file without slices.");
        }

        return ReadThin(path, data);
    }

    private static LinkedBinaryInfo ReadThin(string path, byte[] data)
    {
        if (data.Length < 32 || BinaryData.U32(data, 0) != Magic64)
        {
            throw new FormatException(path + " is not a 64-bit little-endian Mach-O file.");
        }

        var cpuType = (int)BinaryData.U32(data, 4);
        var commandCount = BinaryData.U32(data, 16);
        string ownName = null;
        var libraries = new List<string>();
        var exports = new List<string>();
        var imports = new List<string>();
        var command = 32L;
        for (var i = 0; i < commandCount; i++)
        {
            var kind = BinaryData.U32(data, command);
            var size = BinaryData.U32(data, command + 4);
            if (size < 8)
            {
                throw new FormatException(path + ": a load command is shorter than its header.");
            }

            switch (kind)
            {
                case CommandIdDylib:
                    ownName = BinaryData.CString(data, command + BinaryData.U32(data, command + 8), command + size);
                    break;
                case CommandLoadDylib:
                case CommandLoadWeakDylib:
                case CommandReexportDylib:
                case CommandLazyLoadDylib:
                case CommandLoadUpwardDylib:
                    libraries.Add(BinaryData.CString(data, command + BinaryData.U32(data, command + 8), command + size));
                    break;
                case CommandSymtab:
                    ReadSymbols(data, command, exports, imports);
                    break;
            }

            command += size;
        }

        return new LinkedBinaryInfo
        {
            Path = path,
            Format = LinkedBinaryFormat.MachO,
            Machine = cpuType switch
            {
                CpuTypeX64 => "x86-64",
                CpuTypeArm64 => "arm64",
                _ => "0x" + cpuType.ToString("X8", CultureInfo.InvariantCulture),
            },
            OwnName = ownName,
            LinkedLibraries = libraries,
            ExportedSymbols = exports,
            ImportedSymbols = imports,
        };
    }

    private static void ReadSymbols(byte[] data, long command, List<string> exports, List<string> imports)
    {
        var symbolOffset = (long)BinaryData.U32(data, command + 8);
        var symbolCount = BinaryData.U32(data, command + 12);
        var stringOffset = (long)BinaryData.U32(data, command + 16);
        var stringEnd = stringOffset + BinaryData.U32(data, command + 20);
        for (var i = 0; i < symbolCount; i++)
        {
            var entry = symbolOffset + (i * 16L);
            var nameIndex = BinaryData.U32(data, entry);
            var type = (byte)(BinaryData.U16(data, entry + 4) & 0xFF);
            var isStab = (type & 0xE0) != 0;
            var isExternal = (type & 0x01) != 0;
            var isPrivateExternal = (type & 0x10) != 0;
            var isDefinedInSection = (type & 0x0E) == 0x0E;
            var isUndefined = (type & 0x0E) == 0x00;
            if (!isStab && isExternal && isUndefined && nameIndex != 0)
            {
                var imported = BinaryData.CString(data, stringOffset + nameIndex, stringEnd);
                imports.Add(imported.StartsWith('_') ? imported.Substring(1) : imported);
            }

            if (!isStab && isExternal && !isPrivateExternal && isDefinedInSection && nameIndex != 0)
            {
                var name = BinaryData.CString(data, stringOffset + nameIndex, stringEnd);
                exports.Add(name.StartsWith('_') ? name.Substring(1) : name);
            }
        }
    }
}
