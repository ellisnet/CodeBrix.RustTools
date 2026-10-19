using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Rejects an ELF file that needs a newer glibc than a floor (the glibc of the manylinux image
/// it was built in): the highest <c>GLIBC_x.y</c> symbol version it references is the oldest
/// glibc it loads on.
/// </summary>
internal sealed class GlibcFloorVerifier : IArtifactVerifier
{
    public GlibcFloorVerifier(string floor)
    {
        Floor = Version.Parse(floor ?? throw new ArgumentNullException(nameof(floor)));
    }

    /// <summary>The newest glibc version the file may need.</summary>
    public Version Floor { get; }

    public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
    {
        var name = Path.GetFileName(artifactPath ?? string.Empty);
        ok = true;
        NativeArtifactInfo info;
        try
        {
            info = NativeArtifactReader.Read(artifactPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException or ArgumentException)
        {
            ok = false;
            return new[] { $"{name}: the glibc floor cannot be checked: {ex.Message}" };
        }

        var highest = DefaultArtifactVerifier.HighestGlibcVersion(info);
        if (highest == null)
        {
            return new[] { $"{name}: needs no versioned glibc symbol (floor {Floor})." };
        }

        if (Version.Parse(highest) > Floor)
        {
            ok = false;
            return new[] { $"{name}: needs glibc {highest}, newer than the baseline's {Floor}; it would not load on the older distributions the baseline is for." };
        }

        return new[] { $"{name}: needs glibc {highest} at most, within the baseline's {Floor}." };
    }
}
