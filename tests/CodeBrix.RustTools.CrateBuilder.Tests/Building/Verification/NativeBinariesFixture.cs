using System;
using System.IO;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building.Verification;

/// <summary>
/// Copies the nine per-RID <c>codebrix_wgpu</c> binaries of the referenced
/// CodeBrix.WebGpu.Native package from the NuGet cache into a throw-away folder under the test
/// output (the package folder is never written to or used directly), once per test class.
/// </summary>
public sealed class NativeBinariesFixture : IDisposable
{
    /// <summary>The package version the CrateBuilder project references.</summary>
    public const string PackageVersion = "1.0.290.1433";

    public NativeBinariesFixture()
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packages))
        {
            packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }

        PackageRuntimes = Path.Combine(packages, "codebrix.webgpu.native.mitlicenseforever", PackageVersion, "runtimes");
        Directory = Path.Combine(AppContext.BaseDirectory, "sandboxes", "native-" + Guid.NewGuid().ToString("N"));
        if (!System.IO.Directory.Exists(PackageRuntimes))
        {
            return;
        }

        foreach (var rid in new[] { "linux-x64", "linux-arm64", "linux-riscv64", "android-x64", "android-arm64", "win-x64", "win-arm64", "osx-x64", "osx-arm64" })
        {
            var source = Path.Combine(PackageRuntimes, rid, "native");
            var destination = System.IO.Directory.CreateDirectory(Path.Combine(Directory, rid)).FullName;
            foreach (var file in System.IO.Directory.GetFiles(source, "*codebrix_wgpu*"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
    }

    /// <summary>The package's runtimes folder in the NuGet cache (read only).</summary>
    public string PackageRuntimes { get; }

    /// <summary>The folder holding <c>&lt;rid&gt;/&lt;file&gt;</c> copies.</summary>
    public string Directory { get; }

    /// <summary>Whether the package was found in the cache.</summary>
    public bool Available => System.IO.Directory.Exists(Directory);

    /// <summary>The copy of a RID's library.</summary>
    public string PathOf(string rid)
        => Path.Combine(Directory, rid, rid.StartsWith("win", StringComparison.Ordinal) ? "codebrix_wgpu.dll" : rid.StartsWith("osx", StringComparison.Ordinal) ? "libcodebrix_wgpu.dylib" : "libcodebrix_wgpu.so");

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; the folder lives under bin/.
        }
    }
}
