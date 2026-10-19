using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

public class LinkedBinaryReaderTests
{
    public static TheoryData<CrateTarget, string> PackageLibraries => new TheoryData<CrateTarget, string>
    {
        { CrateTarget.LinuxX64, "x86-64" },
        { CrateTarget.LinuxArm64, "arm64" },
        { CrateTarget.LinuxRiscV64, "riscv64" },
        { CrateTarget.AndroidX64, "x86-64" },
        { CrateTarget.AndroidArm64, "arm64" },
        { CrateTarget.OsxX64, "x86-64" },
        { CrateTarget.OsxArm64, "arm64" },
        { CrateTarget.WinX64, "x86-64" },
        { CrateTarget.WinArm64, "arm64" },
    };

    [Theory]
    [MemberData(nameof(PackageLibraries))]
    public void Read_reads_the_format_machine_name_and_exports_of_the_packaged_engine(CrateTarget target, string machine)
    {
        //Arrange
        var location = WebGpuTestPackage.LocateOrSkip();
        using var sandbox = SampleSandbox.CreateEmpty();
        var copy = Path.Combine(sandbox.Directory, NativePackageLocator.NativeFileName(target));
        File.Copy(location.NativeLibraryPath(target), copy);

        var format = CrateTargets.IsWindows(target) ? LinkedBinaryFormat.Pe : CrateTargets.IsMacOS(target) ? LinkedBinaryFormat.MachO : LinkedBinaryFormat.Elf;

        //Act
        var info = LinkedBinaryReader.Read(copy);

        //Assert
        info.Format.Should().Be(format);
        info.Machine.Should().Be(machine);
        info.ExportedSymbols.Should().Contain("wgpuCodebrixGetApiVersion");
        info.ExportedSymbols.Should().Contain("wgpuCreateInstance");
        info.LinkedLibraries.Should().NotBeEmpty();
        info.LinkedLibraries.Should().NotContain(l => l.Contains("codebrix_wgpu", StringComparison.OrdinalIgnoreCase));
        info.Warnings.Should().BeEmpty();
        var expectedName = format switch
        {
            LinkedBinaryFormat.Elf => "libcodebrix_wgpu.so",
            LinkedBinaryFormat.MachO => "@rpath/libcodebrix_wgpu.dylib",
            _ => "codebrix_wgpu.dll",
        };
        info.OwnName.Should().Be(expectedName);
    }

    [Fact]
    public void Read_of_a_file_that_is_no_library_throws_a_format_error()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile("not-a-library.so", "just text, no magic bytes\n");

        //Act
        var act = () => LinkedBinaryReader.Read(path);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Read_of_a_truncated_elf_file_throws_a_format_error()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = Path.Combine(sandbox.Directory, "truncated.so");
        File.WriteAllBytes(path, new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 });

        //Act
        var act = () => LinkedBinaryReader.Read(path);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Read_of_a_missing_file_throws_file_not_found()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var act = () => LinkedBinaryReader.Read(Path.Combine(sandbox.Directory, "missing.so"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }
}
