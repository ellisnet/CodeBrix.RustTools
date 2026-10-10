using System;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class ToolVersionTests
{
    [Theory]
    [InlineData("cargo 1.94.1 (29ea6fb6a 2026-03-24)", 1, 94, 1, null, "cargo", "29ea6fb6a", "2026-03-24")]
    [InlineData("rustc 1.94.1 (e408947bf 2026-03-25)", 1, 94, 1, null, "rustc", "e408947bf", "2026-03-25")]
    [InlineData("rustup 1.28.2 (e4f3ad6f8 2025-04-28)", 1, 28, 2, null, "rustup", "e4f3ad6f8", "2025-04-28")]
    [InlineData("rustc 1.95.0-nightly (abc123 2026-04-01)", 1, 95, 0, "nightly", "rustc", "abc123", "2026-04-01")]
    [InlineData("rustc 1.80.0-beta.3 (6c1b2c4f8 2024-07-01)", 1, 80, 0, "beta.3", "rustc", "6c1b2c4f8", "2024-07-01")]
    [InlineData("clippy 0.1.94 (e408947bfd 2026-03-25)", 0, 1, 94, null, "clippy", "e408947bfd", "2026-03-25")]
    [InlineData("rustfmt 1.8.0-stable (e408947bfd 2026-03-25)", 1, 8, 0, null, "rustfmt", "e408947bfd", "2026-03-25")]
    [InlineData("rustdoc 1.94.1 (e408947bf 2026-03-25)", 1, 94, 1, null, "rustdoc", "e408947bf", "2026-03-25")]
    [InlineData("0.100.0 (5f94df478 2026-08-27)", 0, 100, 0, null, null, "5f94df478", "2026-08-27")]
    [InlineData("1.29.1", 1, 29, 1, null, null, null, null)]
    [InlineData("rustc 1.94.1", 1, 94, 1, null, "rustc", null, null)]
    [InlineData("rustc 1.94.1 (e408947bf 2026-03-25) (Homebrew)", 1, 94, 1, null, "rustc", "e408947bf", "2026-03-25")]
    [InlineData("cargo 1.75", 1, 75, 0, null, "cargo", null, null)]
    public void Parse_reads_tool_output(string text, int major, int minor, int patch, string preRelease, string toolName, string hash, string date)
    {
        //Act
        var version = ToolVersion.Parse(text);

        //Assert
        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.PreRelease.Should().Be(preRelease);
        version.ToolName.Should().Be(toolName);
        version.CommitHash.Should().Be(hash);
        version.CommitDate.Should().Be(date == null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        version.Raw.Should().Be(text);
    }

    [Theory]
    [InlineData("rustc 1.95.0-nightly (abc123 2026-04-01)", RustChannel.Nightly)]
    [InlineData("rustc 1.95.0-beta.2 (abc123 2026-04-01)", RustChannel.Beta)]
    [InlineData("rustc 1.94.1 (e408947bf 2026-03-25)", RustChannel.Stable)]
    [InlineData("rustfmt 1.8.0-stable (e408947bfd 2026-03-25)", RustChannel.Stable)]
    [InlineData("rustc 1.96.0-dev", RustChannel.Dev)]
    public void Parse_derives_channel_from_pre_release(string text, RustChannel channel)
        => ToolVersion.Parse(text).Channel.Should().Be(channel);

    [Theory]
    [InlineData("cc (Debian 14.2.0-19) 14.2.0", 14, 2, 0)]
    [InlineData("Apple clang version 15.0.0 (clang-1500.3.9.4)", 15, 0, 0)]
    [InlineData("\n\ncargo 1.94.1 (29ea6fb6a 2026-03-24)\ninfo: extra", 1, 94, 1)]
    public void Parse_finds_version_in_free_form_output(string text, int major, int minor, int patch)
    {
        //Act
        var version = ToolVersion.Parse(text);

        //Assert
        (version.Major, version.Minor, version.Patch).Should().Be((major, minor, patch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("error: 'cargo-clippy' is not installed for the toolchain")]
    public void TryParse_returns_false_without_a_version(string text)
    {
        //Act
        var parsed = ToolVersion.TryParse(text, out var version);

        //Assert
        parsed.Should().BeFalse();
        version.Should().BeNull();
    }

    [Fact]
    public void Parse_throws_FormatException_without_a_version()
    {
        //Arrange
        Action act = () => ToolVersion.Parse("no digits here");

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("1.94.1", "1.94.0", 1)]
    [InlineData("1.94.1", "1.99.0", -1)]
    [InlineData("1.95.0-nightly", "1.94.1", 1)]
    [InlineData("1.95.0-nightly", "1.95.0", -1)]
    [InlineData("1.95.0-beta.2", "1.95.0-beta.10", -1)]
    [InlineData("rustfmt 1.8.0-stable (e408947bfd 2026-03-25)", "1.8.0", 0)]
    [InlineData("0.1.94", "0.1.99", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void CompareTo_uses_semantic_version_precedence(string left, string right, int expectedSign)
        => Math.Sign(ToolVersion.Parse(left).CompareTo(ToolVersion.Parse(right))).Should().Be(expectedSign);

    [Fact]
    public void Equals_ignores_tool_name_commit_and_raw_text()
    {
        //Arrange
        var fromTool = ToolVersion.Parse("cargo 1.94.1 (29ea6fb6a 2026-03-24)");
        var bare = ToolVersion.Parse("1.94.1");

        //Act
        var equal = fromTool.Equals(bare);

        //Assert
        equal.Should().BeTrue();
        fromTool.GetHashCode().Should().Be(bare.GetHashCode());
    }

    [Fact]
    public void Comparison_operators_follow_CompareTo()
    {
        //Arrange
        var older = new ToolVersion(1, 94, 1);
        var newer = new ToolVersion(1, 99, 0);

        //Assert
        (older < newer).Should().BeTrue();
        (newer > older).Should().BeTrue();
        (older <= new ToolVersion(1, 94, 1)).Should().BeTrue();
        (newer >= older).Should().BeTrue();
        (null < older).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 94, 0, true)]
    [InlineData(1, 94, 1, true)]
    [InlineData(1, 94, 2, false)]
    [InlineData(1, 70, 0, true)]
    [InlineData(2, 0, 0, false)]
    public void IsAtLeast_compares_numbers(int major, int minor, int patch, bool expected)
        => ToolVersion.Parse("rustc 1.94.1 (e408947bf 2026-03-25)").IsAtLeast(major, minor, patch).Should().Be(expected);

    [Fact]
    public void ToString_is_canonical_version_text()
        => ToolVersion.Parse("rustc 1.95.0-nightly (abc123 2026-04-01)").ToString().Should().Be("1.95.0-nightly");

    [Fact]
    public void Constructor_rejects_negative_numbers()
    {
        //Arrange
        Action act = () => _ = new ToolVersion(1, -1, 0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
