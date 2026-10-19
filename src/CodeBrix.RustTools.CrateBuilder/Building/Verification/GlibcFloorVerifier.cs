using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Rejects an ELF file that needs a newer glibc than a floor (the glibc of the manylinux image
/// it was built in, which differs per target: <see cref="ManyLinux.ManyLinuxImages.GlibcFloor"/>):
/// the highest <c>GLIBC_x.y</c> symbol version it references is the oldest glibc it loads on.
/// </summary>
internal sealed class GlibcFloorVerifier : IArtifactVerifier
{
    private readonly Func<CrateTarget, string> _floorOf;

    /// <summary>A verifier with one floor for every target.</summary>
    public GlibcFloorVerifier(string floor)
    {
        Floor = Version.Parse(floor ?? throw new ArgumentNullException(nameof(floor)));
        _floorOf = _ => floor;
    }

    /// <summary>A verifier whose floor depends on the target (for example
    /// <see cref="ManyLinux.ManyLinuxImages.GlibcFloor"/>).</summary>
    public GlibcFloorVerifier(Func<CrateTarget, string> floorOf)
    {
        _floorOf = floorOf ?? throw new ArgumentNullException(nameof(floorOf));
    }

    /// <summary>The newest glibc version the file may need when the verifier has one floor for
    /// every target; <c>null</c> for a per-target verifier (<see cref="FloorFor"/>).</summary>
    public Version Floor { get; }

    /// <summary>The newest glibc version a file of <paramref name="target"/> may need.</summary>
    public Version FloorFor(CrateTarget target) => Version.Parse(_floorOf(target));

    public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
    {
        var name = Path.GetFileName(artifactPath ?? string.Empty);
        ok = true;
        var floor = FloorFor(target);
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
            return new[] { $"{name}: needs no versioned glibc symbol (floor {floor})." };
        }

        if (Version.Parse(highest) > floor)
        {
            ok = false;
            return new[] { $"{name}: needs glibc {highest}, newer than the {CrateTargets.Rid(target)} baseline's {floor}; it would not load on the older distributions the baseline is for." };
        }

        return new[] { $"{name}: needs glibc {highest} at most, within the {CrateTargets.Rid(target)} baseline's {floor}." };
    }
}
