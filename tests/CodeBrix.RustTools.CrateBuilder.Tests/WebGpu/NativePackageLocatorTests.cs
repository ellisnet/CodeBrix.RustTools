using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

public class NativePackageLocatorTests
{
    private const string Version = "9.8.7.6";

    private static Func<string, string> Environment(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    private static string PackageFolderUnder(string nugetRoot, string version = Version)
        => Path.Combine(nugetRoot, NativePackageLocator.PackageFolderName, version.ToLowerInvariant());

    // ---- names ----

    [Fact]
    public void NativeFileName_is_the_engine_file_name_of_each_operating_system()
    {
        //Act
        var names = CrateTargets.All.ToDictionary(t => t, NativePackageLocator.NativeFileName);

        //Assert
        names[CrateTarget.LinuxX64].Should().Be("libcodebrix_wgpu.so");
        names[CrateTarget.LinuxArm64].Should().Be("libcodebrix_wgpu.so");
        names[CrateTarget.LinuxRiscV64].Should().Be("libcodebrix_wgpu.so");
        names[CrateTarget.AndroidX64].Should().Be("libcodebrix_wgpu.so");
        names[CrateTarget.AndroidArm64].Should().Be("libcodebrix_wgpu.so");
        names[CrateTarget.OsxX64].Should().Be("libcodebrix_wgpu.dylib");
        names[CrateTarget.OsxArm64].Should().Be("libcodebrix_wgpu.dylib");
        names[CrateTarget.WinX64].Should().Be("codebrix_wgpu.dll");
        names[CrateTarget.WinArm64].Should().Be("codebrix_wgpu.dll");
    }

    [Fact]
    public void ReferencedPackageVersion_is_the_managed_assembly_version_without_the_commit_suffix()
    {
        //Act
        var version = NativePackageLocator.ReferencedPackageVersion;

        //Assert
        version.Should().NotBeNullOrWhiteSpace();
        version.Should().NotContain("+");
        version.Should().Be(BuildRecordFactory.ReferencedWebGpuNativeVersion);
        System.Version.TryParse(version, out _).Should().BeTrue();
    }

    [Fact]
    public void NuGetPackageRoots_are_NUGET_PACKAGES_then_HOME_then_USERPROFILE_then_the_profile_folder_without_repeats()
    {
        //Arrange
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", "/cache/pkgs"), ("HOME", "/home/u"), ("USERPROFILE", "/home/u")), "/profile", null);

        //Act
        var roots = locator.NuGetPackageRoots();

        //Assert
        roots.Should().Equal(
            Path.GetFullPath("/cache/pkgs"),
            Path.GetFullPath(Path.Combine("/home/u", ".nuget", "packages")),
            Path.GetFullPath(Path.Combine("/profile", ".nuget", "packages")));
    }

    // ---- the real package ----

    [Fact]
    public void Locate_finds_the_referenced_package_in_this_machines_nuget_cache()
    {
        //Act
        var location = WebGpuTestPackage.LocateOrSkip();

        //Assert
        location.Version.Should().Be(NativePackageLocator.ReferencedPackageVersion);
        location.IsReferencedVersion.Should().BeTrue();
        Path.GetFileName(location.PackageDirectory).Should().Be(NativePackageLocator.ReferencedPackageVersion.ToLowerInvariant());
        foreach (var file in NativePackageLocator.RequiredIncludeFiles)
        {
            File.Exists(Path.Combine(location.IncludeDirectory, file)).Should().BeTrue(file);
        }

        foreach (var target in CrateTargets.All)
        {
            var path = location.NativeLibraryPath(target);
            Path.GetFileName(path).Should().Be(NativePackageLocator.NativeFileName(target));
            path.Should().Be(Path.Combine(location.PackageDirectory, "runtimes", CrateTargets.Rid(target), "native", NativePackageLocator.NativeFileName(target)));
        }
    }

    // ---- search order (fake folders) ----

    [Fact]
    public void Locate_prefers_the_explicit_folder_over_the_nuget_cache()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var nugetRoot = Path.Combine(sandbox.Directory, "nuget");
        WebGpuTestPackage.CreateFake(PackageFolderUnder(nugetRoot), Version, CrateTarget.LinuxX64);
        var explicitFolder = WebGpuTestPackage.CreateFake(Path.Combine(sandbox.Directory, "explicit"), Version, CrateTarget.LinuxX64);
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", nugetRoot)), null, null);

        //Act
        var location = locator.Locate(explicitFolder);

        //Assert
        location.PackageDirectory.Should().Be(explicitFolder);
        location.IncludeDirectory.Should().Be(Path.Combine(explicitFolder, "include"));
        location.NativeLibraryPath(CrateTarget.LinuxX64).Should().StartWith(explicitFolder);
        location.SearchedDirectories.Should().Equal(explicitFolder, PackageFolderUnder(nugetRoot));
    }

    [Fact]
    public void Locate_uses_the_NUGET_PACKAGES_root_and_the_home_root_in_that_order()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var nugetRoot = Path.Combine(sandbox.Directory, "moved-cache");
        var home = Path.Combine(sandbox.Directory, "home");
        WebGpuTestPackage.CreateFake(PackageFolderUnder(Path.Combine(home, ".nuget", "packages")), Version, CrateTarget.LinuxX64);
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", nugetRoot), ("HOME", home)), null, null);

        //Act
        var location = locator.Locate();

        //Assert
        location.PackageDirectory.Should().Be(PackageFolderUnder(Path.Combine(home, ".nuget", "packages")));
        location.SearchedDirectories.First().Should().Be(PackageFolderUnder(nugetRoot));
    }

    [Fact]
    public void Locate_skips_a_folder_whose_include_folder_is_incomplete()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var explicitFolder = WebGpuTestPackage.CreateFake(Path.Combine(sandbox.Directory, "explicit"), Version);
        File.Delete(Path.Combine(explicitFolder, "include", NativePackageLocator.RustProcsFileName));
        var nugetRoot = Path.Combine(sandbox.Directory, "nuget");
        WebGpuTestPackage.CreateFake(PackageFolderUnder(nugetRoot), Version);
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", nugetRoot)), null, null);

        //Act
        var location = locator.Locate(explicitFolder);

        //Assert
        location.PackageDirectory.Should().Be(PackageFolderUnder(nugetRoot));
    }

    [Fact]
    public void Locate_without_an_include_folder_throws_naming_every_folder_searched_and_the_fix()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var nugetRoot = Path.Combine(sandbox.Directory, "nuget");
        var explicitFolder = Path.Combine(sandbox.Directory, "nothing-here");
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", nugetRoot)), null, null);

        //Act
        var act = () => locator.Locate(explicitFolder);

        //Assert
        var thrown = act.Should().Throw<CrateBuilderException>().Which;
        thrown.Stage.Should().Be(CrateBuildStage.Building);
        thrown.Message.Should().Contain(Path.Combine(explicitFolder, "include"));
        thrown.Message.Should().Contain(Path.Combine(PackageFolderUnder(nugetRoot), "include"));
        thrown.Message.Should().Contain("dotnet restore");
        thrown.Message.Should().Contain("WebGpuNativePackageDirectory");
        thrown.Message.Should().Contain(Version);
    }

    [Fact]
    public void Libraries_fall_back_to_the_other_package_folders_then_to_the_application_runtimes_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var explicitFolder = WebGpuTestPackage.CreateFake(Path.Combine(sandbox.Directory, "explicit"), Version);
        var nugetRoot = Path.Combine(sandbox.Directory, "nuget");
        WebGpuTestPackage.CreateFake(PackageFolderUnder(nugetRoot), Version, CrateTarget.LinuxArm64);
        var applicationBase = Path.Combine(sandbox.Directory, "app");
        var appLibrary = WebGpuTestPackage.WriteFakeLibrary(Path.Combine(applicationBase, "runtimes"), CrateTarget.WinX64, "app copy");
        var locator = new NativePackageLocator(Version, Environment(("NUGET_PACKAGES", nugetRoot)), null, applicationBase);

        //Act
        var location = locator.Locate(explicitFolder);

        //Assert
        location.PackageDirectory.Should().Be(explicitFolder);
        location.NativeLibraryPath(CrateTarget.LinuxArm64).Should().StartWith(PackageFolderUnder(nugetRoot));
        location.NativeLibraryPath(CrateTarget.WinX64).Should().Be(appLibrary);
        location.BinaryRoots.Should().Equal(
            Path.Combine(explicitFolder, "runtimes"),
            Path.Combine(PackageFolderUnder(nugetRoot), "runtimes"),
            Path.Combine(applicationBase, "runtimes"));
    }

    [Fact]
    public void An_explicit_folder_of_another_version_reports_its_own_version_and_takes_no_fallback_libraries()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var explicitFolder = WebGpuTestPackage.CreateFake(Path.Combine(sandbox.Directory, "older"), "1.2.3.4", CrateTarget.LinuxX64);
        var applicationBase = Path.Combine(sandbox.Directory, "app");
        WebGpuTestPackage.WriteFakeLibrary(Path.Combine(applicationBase, "runtimes"), CrateTarget.WinX64, "app copy");
        var locator = new NativePackageLocator(Version, Environment(), null, applicationBase);

        //Act
        var location = locator.Locate(explicitFolder);

        //Assert
        location.Version.Should().Be("1.2.3.4");
        location.ReferencedVersion.Should().Be(Version);
        location.IsReferencedVersion.Should().BeFalse();
        location.BinaryRoots.Should().Equal(Path.Combine(explicitFolder, "runtimes"));
        location.TryGetNativeLibraryPath(CrateTarget.WinX64, out _).Should().BeFalse();
    }

    [Fact]
    public void NativeLibraryPath_of_a_missing_library_throws_listing_every_candidate()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var explicitFolder = WebGpuTestPackage.CreateFake(Path.Combine(sandbox.Directory, "explicit"), Version);
        var applicationBase = Path.Combine(sandbox.Directory, "app");
        var location = new NativePackageLocator(Version, Environment(), null, applicationBase).Locate(explicitFolder);

        //Act
        var act = () => location.NativeLibraryPath(CrateTarget.OsxArm64);

        //Assert
        var thrown = act.Should().Throw<CrateBuilderException>().Which;
        thrown.Stage.Should().Be(CrateBuildStage.Building);
        thrown.Message.Should().Contain(Path.Combine(explicitFolder, "runtimes", "osx-arm64", "native", "libcodebrix_wgpu.dylib"));
        thrown.Message.Should().Contain(Path.Combine(applicationBase, "runtimes", "osx-arm64", "native", "libcodebrix_wgpu.dylib"));
    }

    [Fact]
    public void The_constructor_rejects_a_blank_version()
    {
        //Act
        var act = () => new NativePackageLocator(" ", null, null, null);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

}
