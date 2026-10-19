using System;
using System.IO;
using System.Runtime.InteropServices;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

/// <summary>
/// The CodeBrix.WebGpu.Native package of this machine's NuGet cache (the version CrateBuilder
/// references), for tests that read its real files, and fake package folders for pure tests.
/// </summary>
internal static class WebGpuTestPackage
{
    /// <summary>Finds the referenced package with the default locator, or skips the test.</summary>
    public static NativePackageLocation LocateOrSkip()
    {
        try
        {
            return new NativePackageLocator().Locate();
        }
        catch (CrateBuilderException ex)
        {
            Assert.Skip("The CodeBrix.WebGpu.Native package is not in this machine's NuGet cache: " + ex.Message);
            return null;
        }
    }

    /// <summary>The target of the running process, or <c>null</c> on a platform CrateBuilder
    /// does not build for.</summary>
    public static CrateTarget? HostTarget()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => CrateTarget.LinuxX64,
                Architecture.Arm64 => CrateTarget.LinuxArm64,
                Architecture.RiscV64 => CrateTarget.LinuxRiscV64,
                _ => null,
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return architecture == Architecture.Arm64 ? CrateTarget.OsxArm64 : CrateTarget.OsxX64;
        }

        if (OperatingSystem.IsWindows())
        {
            return architecture == Architecture.Arm64 ? CrateTarget.WinArm64 : CrateTarget.WinX64;
        }

        return null;
    }

    /// <summary>
    /// Writes a fake package folder: every required include/ file, a nuspec with
    /// <paramref name="version"/>, and for each given target runtimes/&lt;rid&gt;/native/ with a
    /// fake library and the licence. Returns the folder.
    /// </summary>
    public static string CreateFake(string directory, string version, params CrateTarget[] targets)
    {
        var include = Path.Combine(directory, NativePackageLocator.IncludeFolderName);
        Directory.CreateDirectory(include);
        foreach (var file in NativePackageLocator.RequiredIncludeFiles)
        {
            File.WriteAllText(Path.Combine(include, file), "fake " + file + " " + version + "\n");
        }

        File.WriteAllText(
            Path.Combine(directory, NativePackageLocator.PackageFolderName + ".nuspec"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd\"><metadata><id>"
            + NativePackageLocator.PackageId + "</id><version>" + version + "</version></metadata></package>\n");

        foreach (var target in targets)
        {
            WriteFakeLibrary(Path.Combine(directory, "runtimes"), target, "fake engine " + version);
        }

        return directory;
    }

    /// <summary>Writes runtimes-style &lt;rid&gt;/native/&lt;file&gt; plus the licence under
    /// <paramref name="runtimesDirectory"/> and returns the library path.</summary>
    public static string WriteFakeLibrary(string runtimesDirectory, CrateTarget target, string contents)
    {
        var native = Path.Combine(runtimesDirectory, CrateTargets.Rid(target), "native");
        Directory.CreateDirectory(native);
        var path = Path.Combine(native, NativePackageLocator.NativeFileName(target));
        File.WriteAllText(path, contents + " " + CrateTargets.Rid(target) + "\n");
        File.WriteAllText(Path.Combine(native, NativePackageLocator.LicenseFileName), "fake licence\n");
        return path;
    }
}
