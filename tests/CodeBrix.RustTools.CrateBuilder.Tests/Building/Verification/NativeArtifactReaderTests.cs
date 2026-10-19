using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building.Verification;

public class NativeArtifactReaderTests : IClassFixture<NativeBinariesFixture>
{
    private const string KnownExport = "wgpuCodebrixGetApiVersion";

    private readonly NativeBinariesFixture _binaries;

    public NativeArtifactReaderTests(NativeBinariesFixture binaries)
    {
        _binaries = binaries;
    }

    private NativeArtifactInfo Read(string rid)
    {
        if (!_binaries.Available)
        {
            Assert.Skip("CodeBrix.WebGpu.Native " + NativeBinariesFixture.PackageVersion + " is not in the NuGet cache (" + _binaries.PackageRuntimes + "); restore the CrateBuilder project first.");
        }

        return NativeArtifactReader.Read(_binaries.PathOf(rid));
    }

    [Theory]
    [InlineData("linux-x64", "x86_64", 62)]
    [InlineData("linux-arm64", "aarch64", 183)]
    [InlineData("linux-riscv64", "riscv64", 243)]
    [InlineData("android-x64", "x86_64", 62)]
    [InlineData("android-arm64", "aarch64", 183)]
    public void Read_parses_elf_libraries(string rid, string architecture, int machine)
    {
        //Act
        var info = Read(rid);

        //Assert
        info.Format.Should().Be(NativeArtifactFormat.Elf);
        info.Architecture.Should().Be(architecture);
        info.Machine.Should().Be(machine);
        info.Is64Bit.Should().BeTrue();
        info.IsSharedLibrary.Should().BeTrue();
        info.LibraryName.Should().Be("libcodebrix_wgpu.so");
        info.ExportedSymbols.Should().Contain(KnownExport);
        info.ExportedSymbols.Should().NotContain("malloc");
    }

    [Fact]
    public void Read_lists_the_needed_libraries_and_glibc_versions_of_a_linux_library()
    {
        //Act
        var info = Read("linux-x64");

        //Assert
        info.NeededLibraries.Should().Equal("libdl.so.2", "libgcc_s.so.1", "libpthread.so.0", "libm.so.6", "libc.so.6", "ld-linux-x86-64.so.2");
        info.RequiredSymbolVersions.Should().Contain(v => v.StartsWith("GLIBC_2.", StringComparison.Ordinal));
        DefaultArtifactVerifier.HighestGlibcVersion(info).Should().NotBeNull();
    }

    [Fact]
    public void Read_lists_the_needed_libraries_of_an_android_library()
    {
        //Act
        var info = Read("android-arm64");

        //Assert
        info.NeededLibraries.Should().Contain("libandroid.so");
        info.NeededLibraries.Should().Contain("libc.so");
    }

    [Theory]
    [InlineData("android-x64")]
    [InlineData("android-arm64")]
    public void Read_takes_the_android_api_level_from_the_ndk_note(string rid)
    {
        //Act
        var info = Read(rid);

        //Assert
        info.AndroidApiLevel.Should().Be(33);
    }

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    public void Read_gives_no_android_api_level_for_other_libraries(string rid)
    {
        //Act
        var info = Read(rid);

        //Assert
        info.AndroidApiLevel.Should().BeNull();
    }

    [Theory]
    [InlineData("win-x64", "x86_64", 0x8664)]
    [InlineData("win-arm64", "aarch64", 0xAA64)]
    public void Read_parses_pe_libraries(string rid, string architecture, int machine)
    {
        //Act
        var info = Read(rid);

        //Assert
        info.Format.Should().Be(NativeArtifactFormat.Pe);
        info.Architecture.Should().Be(architecture);
        info.Machine.Should().Be(machine);
        info.Is64Bit.Should().BeTrue();
        info.IsSharedLibrary.Should().BeTrue();
        info.ExportedSymbols.Should().Contain(KnownExport);
        info.NeededLibraries.Should().Contain(n => string.Equals(n, "kernel32.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("osx-x64", "x86_64", 0x01000007)]
    [InlineData("osx-arm64", "aarch64", 0x0100000C)]
    public void Read_parses_mach_o_libraries(string rid, string architecture, int cpuType)
    {
        //Act
        var info = Read(rid);

        //Assert
        info.Format.Should().Be(NativeArtifactFormat.MachO);
        info.Architecture.Should().Be(architecture);
        info.Machine.Should().Be(cpuType);
        info.IsSharedLibrary.Should().BeTrue();
        info.LibraryName.Should().Be("@rpath/libcodebrix_wgpu.dylib");
        info.NeededLibraries.Should().Contain("/usr/lib/libSystem.B.dylib");
        info.ExportedSymbols.Should().Contain(KnownExport);
        info.Notes.Should().Contain(n => n.Contains("LC_SYMTAB"));
    }

    [Fact]
    public void Read_refuses_a_file_that_is_not_native()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile("notes.txt", "not a library");

        //Act
        Action act = () => NativeArtifactReader.Read(path);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Read_reports_a_truncated_elf_file_as_a_format_error()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = Path.Combine(sandbox.Directory, "libtruncated.so");
        File.WriteAllBytes(path, new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0 });

        //Act
        Action act = () => NativeArtifactReader.Read(path);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Read_throws_for_a_missing_file() =>
        ((Action)(() => NativeArtifactReader.Read("/nonexistent/libnothing.so"))).Should().Throw<FileNotFoundException>();

    [Theory]
    [InlineData(CrateTarget.WinArm64, "Pe", "aarch64")]
    [InlineData(CrateTarget.OsxX64, "MachO", "x86_64")]
    [InlineData(CrateTarget.LinuxRiscV64, "Elf", "riscv64")]
    [InlineData(CrateTarget.AndroidX64, "Elf", "x86_64")]
    public void the_expected_format_and_architecture_follow_the_target(CrateTarget target, string format, string architecture)
    {
        //Act
        var expectedFormat = NativeArtifactReader.ExpectedFormat(target);
        var expectedArchitecture = NativeArtifactReader.ExpectedArchitecture(target);

        //Assert
        expectedFormat.ToString().Should().Be(format);
        expectedArchitecture.Should().Be(architecture);
    }
}
