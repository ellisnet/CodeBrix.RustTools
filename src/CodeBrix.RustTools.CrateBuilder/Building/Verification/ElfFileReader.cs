using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Reads a little-endian ELF64 file through its section headers: the machine, the dynamic
/// section (<c>DT_SONAME</c>, <c>DT_NEEDED</c>), the dynamic symbol table and the needed symbol
/// versions. A stripped library keeps all of these (they are what the dynamic loader uses).
/// An Android library also carries the NDK's <c>.note.android.ident</c> note with the API level
/// it was linked for.
/// </summary>
internal static class ElfFileReader
{
    private const ushort EtDyn = 3;
    private const uint ShtDynamic = 6;
    private const uint ShtNote = 7;
    private const uint NtAndroidVersion = 1;
    private const uint ShtDynsym = 11;
    private const uint ShtGnuVerneed = 0x6ffffffe;
    private const long DtNeeded = 1;
    private const long DtSoname = 14;

    public static bool IsElf(BinaryView view)
        => view.StartsWith(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' });

    public static void Read(BinaryView view, NativeArtifactInfo info)
    {
        info.Format = NativeArtifactFormat.Elf;
        var notes = new List<string>();
        var elfClass = view.U8(4);
        var data = view.U8(5);
        info.Machine = view.U16(18);
        info.Architecture = ArchitectureOf(info.Machine);
        if (data != 1)
        {
            notes.Add("Big-endian ELF files are not read beyond the header.");
            info.Notes = notes;
            return;
        }

        if (elfClass != 2)
        {
            info.Is64Bit = false;
            info.IsSharedLibrary = view.U16(16) == EtDyn;
            notes.Add("32-bit ELF files are not read beyond the header.");
            info.Notes = notes;
            return;
        }

        info.Is64Bit = true;
        info.IsSharedLibrary = view.U16(16) == EtDyn;
        var shoff = BinaryView.Checked(view.U64(0x28));
        var shentsize = view.U16(0x3A);
        var shnum = view.U16(0x3C);
        if (shoff == 0 || shnum == 0)
        {
            notes.Add("The file has no section headers; its dynamic section and symbols were not read.");
            info.Notes = notes;
            return;
        }

        if (shentsize < 64)
        {
            throw new FormatException($"The ELF section header size ({shentsize}) is too small.");
        }

        var sections = new List<Section>();
        for (var i = 0; i < shnum; i++)
        {
            var at = shoff + (long)i * shentsize;
            sections.Add(new Section(
                view.U32(at + 4),
                BinaryView.Checked(view.U64(at + 24)),
                BinaryView.Checked(view.U64(at + 32)),
                view.U32(at + 40),
                BinaryView.Checked(view.U64(at + 56))));
        }

        var needed = new List<string>();
        foreach (var dynamic in sections.Where(s => s.Type == ShtDynamic))
        {
            var strings = StringTable(sections, dynamic.Link);
            var entsize = dynamic.EntrySize == 0 ? 16 : dynamic.EntrySize;
            for (long at = dynamic.Offset; at + 16 <= dynamic.Offset + dynamic.Size; at += entsize)
            {
                var tag = view.I64(at);
                if (tag == 0)
                {
                    break;
                }

                if (tag == DtNeeded || tag == DtSoname)
                {
                    var name = view.CString(strings.Offset + BinaryView.Checked(view.U64(at + 8)), strings.Offset + strings.Size);
                    if (tag == DtNeeded)
                    {
                        needed.Add(name);
                    }
                    else
                    {
                        info.LibraryName = name;
                    }
                }
            }
        }

        var exports = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var symbols in sections.Where(s => s.Type == ShtDynsym))
        {
            var strings = StringTable(sections, symbols.Link);
            var entsize = symbols.EntrySize == 0 ? 24 : symbols.EntrySize;
            for (long at = symbols.Offset; at + 24 <= symbols.Offset + symbols.Size; at += entsize)
            {
                var nameOffset = view.U32(at);
                var stInfo = view.U8(at + 4);
                var other = view.U8(at + 5);
                var sectionIndex = view.U16(at + 6);
                var binding = stInfo >> 4;
                var type = stInfo & 0xF;
                var visibility = other & 0x3;
                var defined = sectionIndex != 0;
                var global = binding == 1 || binding == 2 || binding == 10;
                var visible = visibility == 0 || visibility == 3;
                var namedKind = type != 3 && type != 4;
                if (nameOffset != 0 && defined && global && visible && namedKind)
                {
                    exports.Add(view.CString(strings.Offset + nameOffset, strings.Offset + strings.Size));
                }
            }
        }

        var versions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var verneed in sections.Where(s => s.Type == ShtGnuVerneed))
        {
            var strings = StringTable(sections, verneed.Link);
            long entry = verneed.Offset;
            for (var guard = 0; guard < 4096; guard++)
            {
                var count = view.U16(entry + 2);
                var aux = entry + view.U32(entry + 8);
                for (var j = 0; j < count; j++)
                {
                    versions.Add(view.CString(strings.Offset + view.U32(aux + 8), strings.Offset + strings.Size));
                    var nextAux = view.U32(aux + 12);
                    if (nextAux == 0)
                    {
                        break;
                    }

                    aux += nextAux;
                }

                var next = view.U32(entry + 12);
                if (next == 0)
                {
                    break;
                }

                entry += next;
            }
        }

        foreach (var note in sections.Where(s => s.Type == ShtNote))
        {
            info.AndroidApiLevel ??= AndroidApiLevelOf(view, note);
        }

        info.NeededLibraries = needed;
        info.ExportedSymbols = exports.ToList();
        info.RequiredSymbolVersions = versions.ToList();
        info.Notes = notes;
    }

    /// <summary>The architecture name of an ELF <c>e_machine</c> value.</summary>
    public static string ArchitectureOf(int machine) => machine switch
    {
        62 => "x86_64",
        183 => "aarch64",
        243 => "riscv64",
        3 => "x86",
        40 => "arm",
        _ => NativeArtifactInfo.UnknownArchitecture,
    };

    /// <summary>The API level of the NDK's Android note (owner "Android", type 1, the level as
    /// the first 32-bit word of its description) in a note section, or <c>null</c>.</summary>
    private static int? AndroidApiLevelOf(BinaryView view, Section note)
    {
        var at = note.Offset;
        var end = note.Offset + note.Size;
        while (at + 12 <= end)
        {
            var nameSize = view.U32(at);
            var descSize = view.U32(at + 4);
            var type = view.U32(at + 8);
            var name = at + 12;
            var desc = name + Align4(nameSize);
            var next = desc + Align4(descSize);
            if (next > end)
            {
                return null;
            }

            if (type == NtAndroidVersion && nameSize == 8 && descSize >= 4 && view.CString(name, name + nameSize) == "Android")
            {
                return (int)view.U32(desc);
            }

            at = next;
        }

        return null;
    }

    private static long Align4(uint size) => (size + 3L) & ~3L;

    private static Section StringTable(List<Section> sections, uint index)
    {
        if (index >= sections.Count)
        {
            throw new FormatException($"An ELF section links to string table {index}, which does not exist.");
        }

        return sections[(int)index];
    }

    private sealed record Section(uint Type, long Offset, long Size, uint Link, long EntrySize);
}
