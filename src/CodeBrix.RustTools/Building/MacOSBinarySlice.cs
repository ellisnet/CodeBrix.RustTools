using System;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// One architecture slice of a Mach-O file: the whole file for a single-architecture
/// ("thin") binary, or one entry of a universal ("fat") binary's header.
/// </summary>
public sealed class MacOSBinarySlice
{
    /// <summary>The architecture name Apple's tools print: <c>arm64</c>, <c>x86_64</c>, or
    /// <c>cputype 0x...</c> for anything else.</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>The Mach-O <c>cputype</c> value (<c>0x0100000C</c> for arm64, <c>0x01000007</c>
    /// for x86_64).</summary>
    public int CpuType { get; init; }

    /// <summary>The Mach-O <c>cpusubtype</c> value, copied from the slice.</summary>
    public int CpuSubtype { get; init; }

    /// <summary>Where the slice starts in the file (0 for a thin binary).</summary>
    public long Offset { get; init; }

    /// <summary>The slice's length in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The slice's alignment in a universal file, as a power of two (14 means
    /// 16 KiB); 0 for a thin binary.</summary>
    public int Alignment { get; init; }

    /// <summary>Returns the architecture, offset and size.</summary>
    public override string ToString() => $"{Architecture} at {Offset} ({Size} bytes)";
}
