using System;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class HostOperatingSystemTests
{
    [Theory]
    [InlineData("ID=debian\nVERSION_ID=\"13\"", OperatingSystemKind.DebianLinux)]
    [InlineData("NAME=\"Linux Mint\"\nID=linuxmint\nID_LIKE=debian", OperatingSystemKind.DebianLinux)]
    [InlineData("ID=ubuntu\nID_LIKE=debian", OperatingSystemKind.DebianLinux)]
    [InlineData("ID=pop\nID_LIKE=\"ubuntu debian\"", OperatingSystemKind.DebianLinux)]
    [InlineData("ID=raspbian\nID_LIKE=debian", OperatingSystemKind.DebianLinux)]
    [InlineData("ID=fedora\nVERSION_ID=41", OperatingSystemKind.OtherLinux)]
    [InlineData("ID=arch", OperatingSystemKind.OtherLinux)]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"", OperatingSystemKind.OtherLinux)]
    [InlineData("ID=debianish-but-not", OperatingSystemKind.OtherLinux)]
    [InlineData("", OperatingSystemKind.OtherLinux)]
    public void ClassifyOsRelease_detects_debian_family(string osRelease, OperatingSystemKind expected)
        => HostOperatingSystem.ClassifyOsRelease(osRelease).Should().Be(expected);

    [Fact]
    public void ParseOsRelease_strips_quotes_and_skips_comments()
    {
        //Arrange
        const string text = "# comment\nPRETTY_NAME=\"Linux Mint 7\"\nID='lmde'\n\nBROKEN\n";

        //Act
        var values = HostOperatingSystem.ParseOsRelease(text);

        //Assert
        values["PRETTY_NAME"].Should().Be("Linux Mint 7");
        values["ID"].Should().Be("lmde");
        values.Count.Should().Be(2);
    }

    [Fact]
    public void Detect_matches_the_running_platform()
    {
        //Act
        var kind = HostOperatingSystem.Detect();

        //Assert
        if (OperatingSystem.IsWindows())
        {
            kind.Should().Be(OperatingSystemKind.Windows);
        }
        else if (OperatingSystem.IsMacOS())
        {
            kind.Should().Be(OperatingSystemKind.MacOS);
        }
        else if (OperatingSystem.IsLinux())
        {
            kind.Should().BeOneOf(OperatingSystemKind.DebianLinux, OperatingSystemKind.OtherLinux);
        }
    }
}
