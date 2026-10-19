using System;
using System.IO;
using CodeBrix.RustTools.Docker.Artifacts;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Where a target's native files go under the output folder: <c>runtimes/&lt;rid&gt;/native/</c>
/// (<see cref="ArtifactLayout.NuGetRuntimes"/>) or the folder itself
/// (<see cref="ArtifactLayout.Flat"/>) - the same folders CodeBrix.RustTools.Docker's retrieval
/// writes, so both modes lay files out alike.
/// </summary>
internal static class OutputLayout
{
    /// <summary>The folder, relative to the output folder with forward slashes ("" for
    /// Flat).</summary>
    public static string RelativeDirectory(ArtifactLayout layout, CrateTarget target) => layout switch
    {
        ArtifactLayout.Flat => string.Empty,
        ArtifactLayout.NuGetRuntimes => "runtimes/" + CrateTargets.Rid(target) + "/native",
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "CrateBuilder lays files out as NuGetRuntimes or Flat only."),
    };

    /// <summary>The absolute folder of a target's files.</summary>
    public static string DirectoryFor(string outputDirectory, ArtifactLayout layout, CrateTarget target)
    {
        var relative = RelativeDirectory(layout, target);
        var root = Path.GetFullPath(outputDirectory);
        return relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
