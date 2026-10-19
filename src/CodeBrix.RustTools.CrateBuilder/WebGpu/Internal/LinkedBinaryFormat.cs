namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>The container format of a native library read by <see cref="LinkedBinaryReader"/>.</summary>
internal enum LinkedBinaryFormat
{
    /// <summary>ELF (Linux, Android).</summary>
    Elf,

    /// <summary>PE / COFF (Windows).</summary>
    Pe,

    /// <summary>Mach-O (macOS).</summary>
    MachO,
}
