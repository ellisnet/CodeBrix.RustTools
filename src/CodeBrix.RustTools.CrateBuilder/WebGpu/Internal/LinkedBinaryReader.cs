using System;
using System.IO;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>
/// Reads the link facts of an ELF, PE or Mach-O library with no external tool. Kept small on
/// purpose: it reads only what <see cref="WebGpuLinkVerifier"/> checks.
/// </summary>
internal static class LinkedBinaryReader
{
    /// <summary>Reads a file, choosing the reader by its magic bytes.</summary>
    /// <param name="path">The library.</param>
    /// <param name="preferredMachine">For a universal Mach-O file: the slice to read
    /// (<c>x86-64</c> / <c>arm64</c>); <c>null</c> = the first.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The file is none of the three formats, or is
    /// damaged.</exception>
    public static LinkedBinaryInfo Read(string path, string preferredMachine = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The native library does not exist.", path);
        }

        var data = File.ReadAllBytes(path);
        if (ElfReader.IsElf(data))
        {
            return ElfReader.Read(path, data);
        }

        if (PeReader.IsPe(data))
        {
            return PeReader.Read(path, data);
        }

        if (MachOReader.IsMachO(data))
        {
            return MachOReader.Read(path, data, preferredMachine);
        }

        throw new FormatException(path + " is not an ELF, PE or Mach-O file.");
    }
}
