using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Reads and writes universal ("fat") macOS binaries: one file that holds an
/// <c>x86_64</c> slice and an <c>arm64</c> slice of the same program or library, which macOS
/// runs on Intel and Apple Silicon Macs alike. rustc and cargo produce single-architecture
/// files; this class joins them the way Apple's <c>lipo</c> does (a big-endian fat header,
/// slices ordered by <c>cputype</c>, each at an alignment of 4 KiB for x86_64 and 16 KiB for
/// arm64), with no external tool, on any operating system. The slices are copied unchanged,
/// so their own code signatures stay valid. Both CodeBrix.RustTools (on a Mac) and
/// CodeBrix.RustTools.Docker (in a Linux container) use this code, which is what makes their
/// universal files identical when their slices are.
/// </summary>
public static class MacOSUniversalBinary
{
    /// <summary>The magic number that starts a universal file (stored big-endian).</summary>
    public const uint FatMagic = 0xCAFEBABE;

    /// <summary>The Mach-O <c>cputype</c> of x86_64.</summary>
    public const int CpuTypeX64 = 0x01000007;

    /// <summary>The Mach-O <c>cputype</c> of arm64.</summary>
    public const int CpuTypeArm64 = 0x0100000C;

    private const uint MachO64Magic = 0xFEEDFACF;
    private const uint MachO32Magic = 0xFEEDFACE;
    private const int FatArchSize = 20;

    /// <summary>
    /// Joins single-architecture Mach-O files into one universal file.
    /// </summary>
    /// <param name="slicePaths">Two or more thin Mach-O files of distinct architectures (for
    /// example the arm64 and x86_64 builds of one program).</param>
    /// <param name="outputPath">The universal file to write; its folder is created.</param>
    /// <param name="overwrite">Whether an existing output file is replaced.</param>
    /// <returns>The slices as laid out in the output, in file order.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Fewer than two slices, a slice that is not a thin
    /// Mach-O file, or two slices of the same architecture.</exception>
    /// <exception cref="IOException">The output exists and <paramref name="overwrite"/> is
    /// false, or a file cannot be read or written.</exception>
    public static IReadOnlyList<MacOSBinarySlice> Create(IEnumerable<string> slicePaths, string outputPath, bool overwrite = true)
    {
        if (slicePaths == null)
        {
            throw new ArgumentNullException(nameof(slicePaths));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("The output path is required.", nameof(outputPath));
        }

        var inputs = slicePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToList();
        if (inputs.Count < 2)
        {
            throw new ArgumentException("A universal binary needs at least two slices.", nameof(slicePaths));
        }

        var slices = new List<(MacOSBinarySlice Slice, string Path)>();
        foreach (var input in inputs)
        {
            var found = ReadSlices(input);
            if (found.Count != 1 || found[0].Offset != 0)
            {
                throw new ArgumentException($"'{input}' is not a single-architecture Mach-O file; a universal file cannot be a slice.", nameof(slicePaths));
            }

            slices.Add((found[0], input));
        }

        var duplicate = slices.GroupBy(s => s.Slice.CpuType).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new ArgumentException($"Two slices have the same architecture ({ArchitectureName(duplicate.Key)}): {string.Join(", ", duplicate.Select(s => s.Path))}.", nameof(slicePaths));
        }

        var output = Path.GetFullPath(outputPath);
        if (!overwrite && File.Exists(output))
        {
            throw new IOException($"'{output}' already exists.");
        }

        var ordered = slices.OrderBy(s => s.Slice.CpuType).ToList();
        var headerSize = 8 + FatArchSize * ordered.Count;
        var laidOut = new List<(MacOSBinarySlice Slice, string Path)>();
        long position = headerSize;
        foreach (var (slice, path) in ordered)
        {
            var alignment = slice.CpuType == CpuTypeArm64 ? 14 : 12;
            var unit = 1L << alignment;
            var offset = (position + unit - 1) / unit * unit;
            laidOut.Add((new MacOSBinarySlice
            {
                Architecture = slice.Architecture,
                CpuType = slice.CpuType,
                CpuSubtype = slice.CpuSubtype,
                Offset = offset,
                Size = slice.Size,
                Alignment = alignment,
            }, path));
            position = offset + slice.Size;
        }

        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), FatMagic);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)laidOut.Count);
        for (var i = 0; i < laidOut.Count; i++)
        {
            var entry = header.AsSpan(8 + i * FatArchSize, FatArchSize);
            var slice = laidOut[i].Slice;
            BinaryPrimitives.WriteInt32BigEndian(entry.Slice(0, 4), slice.CpuType);
            BinaryPrimitives.WriteInt32BigEndian(entry.Slice(4, 4), slice.CpuSubtype);
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(8, 4), checked((uint)slice.Offset));
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(12, 4), checked((uint)slice.Size));
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(16, 4), (uint)slice.Alignment);
        }

        using (var stream = new FileStream(output, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(header);
            foreach (var (slice, path) in laidOut)
            {
                stream.SetLength(slice.Offset);
                stream.Seek(slice.Offset, SeekOrigin.Begin);
                using var input = File.OpenRead(path);
                input.CopyTo(stream);
            }
        }

        return laidOut.Select(l => l.Slice).ToList();
    }

    /// <summary>
    /// Describes the architecture slices of a Mach-O file: one entry for a thin file, one per
    /// slice for a universal file.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The file is not a Mach-O file, or its fat header is
    /// malformed.</exception>
    public static IReadOnlyList<MacOSBinarySlice> ReadSlices(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path is required.", nameof(path));
        }

        using var stream = File.OpenRead(path);
        var head = new byte[8];
        var read = stream.Read(head, 0, head.Length);
        if (read < 8)
        {
            throw new FormatException($"'{path}' is too short to be a Mach-O file.");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0, 4)) == FatMagic)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4, 4));
            if (count == 0 || count > 64)
            {
                throw new FormatException($"'{path}' has a malformed universal header ({count} slices).");
            }

            var entries = new byte[FatArchSize * count];
            if (stream.Read(entries, 0, entries.Length) != entries.Length)
            {
                throw new FormatException($"'{path}' has a truncated universal header.");
            }

            var slices = new List<MacOSBinarySlice>();
            for (var i = 0; i < count; i++)
            {
                var entry = entries.AsSpan(i * FatArchSize, FatArchSize);
                var cpuType = BinaryPrimitives.ReadInt32BigEndian(entry.Slice(0, 4));
                slices.Add(new MacOSBinarySlice
                {
                    Architecture = ArchitectureName(cpuType),
                    CpuType = cpuType,
                    CpuSubtype = BinaryPrimitives.ReadInt32BigEndian(entry.Slice(4, 4)),
                    Offset = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8, 4)),
                    Size = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(12, 4)),
                    Alignment = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(16, 4)),
                });
            }

            return slices;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0, 4));
        if (magic != MachO64Magic && magic != MachO32Magic)
        {
            throw new FormatException($"'{path}' is not a Mach-O file.");
        }

        var thinHeader = new byte[12];
        stream.Seek(0, SeekOrigin.Begin);
        if (stream.Read(thinHeader, 0, thinHeader.Length) != thinHeader.Length)
        {
            throw new FormatException($"'{path}' is too short to be a Mach-O file.");
        }

        var type = BinaryPrimitives.ReadInt32LittleEndian(thinHeader.AsSpan(4, 4));
        return new[]
        {
            new MacOSBinarySlice
            {
                Architecture = ArchitectureName(type),
                CpuType = type,
                CpuSubtype = BinaryPrimitives.ReadInt32LittleEndian(thinHeader.AsSpan(8, 4)),
                Offset = 0,
                Size = stream.Length,
                Alignment = 0,
            },
        };
    }

    /// <summary>
    /// Whether a file starts with the universal (fat) magic number.
    /// </summary>
    /// <param name="path">The file to test.</param>
    public static bool IsUniversal(string path) => TryReadMagic(path, out var magic, out _) && magic == FatMagic;

    /// <summary>
    /// Whether a file is a Mach-O file, thin or universal.
    /// </summary>
    /// <param name="path">The file to test.</param>
    public static bool IsMachO(string path)
        => TryReadMagic(path, out var bigEndian, out var littleEndian)
           && (bigEndian == FatMagic || littleEndian == MachO64Magic || littleEndian == MachO32Magic);

    /// <summary>
    /// The architecture name of a <c>cputype</c> value, as Apple's tools print it.
    /// </summary>
    /// <param name="cpuType">A Mach-O <c>cputype</c>.</param>
    public static string ArchitectureName(int cpuType) => cpuType switch
    {
        CpuTypeArm64 => "arm64",
        CpuTypeX64 => "x86_64",
        _ => "cputype 0x" + cpuType.ToString("X8"),
    };

    private static bool TryReadMagic(string path, out uint bigEndian, out uint littleEndian)
    {
        bigEndian = 0;
        littleEndian = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var head = new byte[4];
        if (stream.Read(head, 0, 4) != 4)
        {
            return false;
        }

        bigEndian = BinaryPrimitives.ReadUInt32BigEndian(head);
        littleEndian = BinaryPrimitives.ReadUInt32LittleEndian(head);
        return true;
    }
}
