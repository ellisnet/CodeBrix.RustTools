using System;
using System.IO;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Reads ELF (Linux, Android), PE (Windows) and Mach-O (macOS) files with managed code only -
/// no readelf, objdump or otool - into a <see cref="NativeArtifactInfo"/>.
/// </summary>
internal static class NativeArtifactReader
{
    /// <summary>
    /// Reads a native file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The file is not ELF, PE or Mach-O, or it is truncated
    /// or corrupt.</exception>
    public static NativeArtifactInfo Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("The native file does not exist.", full);
        }

        var view = new BinaryView(File.ReadAllBytes(full));
        var info = new NativeArtifactInfo { Path = full };
        if (ElfFileReader.IsElf(view))
        {
            ElfFileReader.Read(view, info);
        }
        else if (PeFileReader.IsPe(view))
        {
            PeFileReader.Read(view, info);
        }
        else if (MachOFileReader.IsThin64(view))
        {
            MachOFileReader.Read(view, info);
        }
        else if (MachOFileReader.IsUniversal(view))
        {
            MachOFileReader.ReadUniversal(full, info);
        }
        else
        {
            throw new FormatException($"'{full}' is not an ELF, PE or 64-bit Mach-O file.");
        }

        return info;
    }

    /// <summary>The format a target's native files have.</summary>
    public static NativeArtifactFormat ExpectedFormat(CrateTarget target)
        => CrateTargets.IsWindows(target) ? NativeArtifactFormat.Pe
            : CrateTargets.IsMacOS(target) ? NativeArtifactFormat.MachO
            : NativeArtifactFormat.Elf;

    /// <summary>The architecture name (as <see cref="NativeArtifactInfo.Architecture"/>) a
    /// target's native files have.</summary>
    public static string ExpectedArchitecture(CrateTarget target) => target switch
    {
        CrateTarget.WinX64 or CrateTarget.OsxX64 or CrateTarget.LinuxX64 or CrateTarget.AndroidX64 => "x86_64",
        CrateTarget.WinArm64 or CrateTarget.OsxArm64 or CrateTarget.LinuxArm64 or CrateTarget.AndroidArm64 => "aarch64",
        CrateTarget.LinuxRiscV64 => "riscv64",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown crate target."),
    };
}
