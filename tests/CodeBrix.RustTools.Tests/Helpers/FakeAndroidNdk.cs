using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// Writes the skeleton of an Android NDK (source.properties and the toolchain bin folder with
/// empty clang wrappers and llvm-ar) so that discovery and the link profile can be tested
/// without a real NDK.
/// </summary>
internal static class FakeAndroidNdk
{
    public static readonly string[] Triples = { "aarch64-linux-android", "x86_64-linux-android" };

    /// <summary>Creates an NDK root at <paramref name="root"/> and returns it.</summary>
    public static string Create(string root, string version, string hostTag, IEnumerable<int> apiLevels)
    {
        var windows = hostTag.StartsWith("windows", System.StringComparison.OrdinalIgnoreCase);
        var bin = Path.Combine(root, "toolchains", "llvm", "prebuilt", hostTag, "bin");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(root, "toolchains", "llvm", "prebuilt", hostTag, "sysroot"));
        if (version != null)
        {
            File.WriteAllText(Path.Combine(root, "source.properties"), "Pkg.Desc = Android NDK\nPkg.Revision = " + version + "\n");
        }

        foreach (var level in apiLevels)
        {
            foreach (var triple in Triples)
            {
                File.WriteAllText(Path.Combine(bin, $"{triple}{level}-clang" + (windows ? ".cmd" : string.Empty)), string.Empty);
                File.WriteAllText(Path.Combine(bin, $"{triple}{level}-clang++" + (windows ? ".cmd" : string.Empty)), string.Empty);
            }
        }

        File.WriteAllText(Path.Combine(bin, windows ? "llvm-ar.exe" : "llvm-ar"), string.Empty);
        File.WriteAllText(Path.Combine(bin, windows ? "llvm-ranlib.exe" : "llvm-ranlib"), string.Empty);
        return root;
    }

    /// <summary>Creates an SDK root holding the given side-by-side NDK versions.</summary>
    public static string CreateSdk(string root, string hostTag, params string[] versions)
    {
        foreach (var version in versions)
        {
            Create(Path.Combine(root, "ndk", version), version, hostTag, new[] { 21, 24 });
        }

        return root;
    }

    public static IReadOnlyList<int> Levels(params int[] levels) => levels.ToList();
}
