namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>The container format of a native file.</summary>
internal enum NativeArtifactFormat
{
    /// <summary>Not a format the reader knows.</summary>
    Unknown,

    /// <summary>ELF (Linux, Android).</summary>
    Elf,

    /// <summary>PE / COFF (Windows).</summary>
    Pe,

    /// <summary>A thin (single-architecture) Mach-O file (macOS).</summary>
    MachO,

    /// <summary>A universal (fat) Mach-O file holding several slices.</summary>
    MachOUniversal,
}
