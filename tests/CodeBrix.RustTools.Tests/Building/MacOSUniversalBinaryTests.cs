using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class MacOSUniversalBinaryTests
{
    private static string WriteThin(string directory, string name, int cpuType, int cpuSubtype, int length)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), cpuType);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), cpuSubtype);
        for (var i = 12; i < length; i++)
        {
            bytes[i] = (byte)((i * 7 + cpuType) & 0xFF);
        }

        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Create_lays_out_x86_64_then_arm64_at_their_alignments_and_round_trips_the_slices()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var arm64 = WriteThin(sandbox.Directory, "arm64", MacOSUniversalBinary.CpuTypeArm64, 0, 3000);
        var x64 = WriteThin(sandbox.Directory, "x86_64", MacOSUniversalBinary.CpuTypeX64, 3, 5000);
        var output = Path.Combine(sandbox.Directory, "out", "universal");

        //Act
        var written = MacOSUniversalBinary.Create(new[] { arm64, x64 }, output);
        var read = MacOSUniversalBinary.ReadSlices(output);

        //Assert
        written.Select(s => s.Architecture).Should().Equal("x86_64", "arm64");
        written[0].Offset.Should().Be(4096);
        written[0].Size.Should().Be(5000);
        written[0].Alignment.Should().Be(12);
        written[0].CpuSubtype.Should().Be(3);
        written[1].Offset.Should().Be(16384);
        written[1].Size.Should().Be(3000);
        written[1].Alignment.Should().Be(14);
        new FileInfo(output).Length.Should().Be(16384 + 3000);
        read.Select(s => (s.Architecture, s.Offset, s.Size, s.Alignment)).Should().Equal(written.Select(s => (s.Architecture, s.Offset, s.Size, s.Alignment)));
        MacOSUniversalBinary.IsUniversal(output).Should().BeTrue();
        MacOSUniversalBinary.IsMachO(output).Should().BeTrue();
        var all = File.ReadAllBytes(output);
        all.AsSpan(4096, 5000).ToArray().Should().Equal(File.ReadAllBytes(x64));
        all.AsSpan(16384, 3000).ToArray().Should().Equal(File.ReadAllBytes(arm64));
        all.AsSpan(48, 4096 - 48).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void ReadSlices_describes_a_thin_file()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var thin = WriteThin(sandbox.Directory, "thin", MacOSUniversalBinary.CpuTypeArm64, 0, 640);

        //Act
        var slices = MacOSUniversalBinary.ReadSlices(thin);

        //Assert
        slices.Should().ContainSingle();
        slices[0].Architecture.Should().Be("arm64");
        slices[0].Offset.Should().Be(0);
        slices[0].Size.Should().Be(640);
        slices[0].Alignment.Should().Be(0);
        MacOSUniversalBinary.IsUniversal(thin).Should().BeFalse();
        MacOSUniversalBinary.IsMachO(thin).Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_a_single_slice_duplicate_architectures_and_non_mach_o_input()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var a = WriteThin(sandbox.Directory, "a", MacOSUniversalBinary.CpuTypeArm64, 0, 100);
        var b = WriteThin(sandbox.Directory, "b", MacOSUniversalBinary.CpuTypeArm64, 0, 100);
        var text = Path.Combine(sandbox.Directory, "text");
        File.WriteAllText(text, "not a binary at all, just text");
        var output = Path.Combine(sandbox.Directory, "universal");

        //Act
        var one = () => MacOSUniversalBinary.Create(new[] { a }, output);
        var same = () => MacOSUniversalBinary.Create(new[] { a, b }, output);
        var notMachO = () => MacOSUniversalBinary.Create(new[] { a, text }, output);

        //Assert
        one.Should().Throw<ArgumentException>().WithMessage("*at least two*");
        same.Should().Throw<ArgumentException>().WithMessage("*same architecture*arm64*");
        notMachO.Should().Throw<FormatException>();
        MacOSUniversalBinary.IsMachO(text).Should().BeFalse();
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_a_universal_file_as_a_slice_and_honours_overwrite()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var arm64 = WriteThin(sandbox.Directory, "arm64", MacOSUniversalBinary.CpuTypeArm64, 0, 300);
        var x64 = WriteThin(sandbox.Directory, "x86_64", MacOSUniversalBinary.CpuTypeX64, 3, 300);
        var output = Path.Combine(sandbox.Directory, "universal");
        MacOSUniversalBinary.Create(new[] { arm64, x64 }, output);

        //Act
        var nested = () => MacOSUniversalBinary.Create(new[] { output, x64 }, Path.Combine(sandbox.Directory, "nested"));
        var existing = () => MacOSUniversalBinary.Create(new[] { arm64, x64 }, output, overwrite: false);

        //Assert
        nested.Should().Throw<ArgumentException>().WithMessage("*not a single-architecture*");
        existing.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData(MacOSUniversalBinary.CpuTypeArm64, "arm64")]
    [InlineData(MacOSUniversalBinary.CpuTypeX64, "x86_64")]
    [InlineData(18, "cputype 0x00000012")]
    public void ArchitectureName_matches_apple_s_names(int cpuType, string expected)
        => MacOSUniversalBinary.ArchitectureName(cpuType).Should().Be(expected);
}
