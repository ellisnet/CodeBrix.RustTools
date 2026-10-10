using System;
using System.IO;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class RustToolchainFileTests
{
    [Fact]
    public void Parse_reads_the_toolchain_table()
    {
        //Arrange
        const string text = """
            [toolchain]
            channel = "nightly-2026-01-15"
            components = ["rustfmt", "clippy", "rust-src"]
            targets = ["wasm32-unknown-unknown", "aarch64-unknown-linux-gnu"]
            profile = "minimal"
            """;

        //Act
        var file = RustToolchainFile.Parse(text, "/work/rust-toolchain.toml");

        //Assert
        file.Channel.Should().Be("nightly-2026-01-15");
        file.ChannelKind.Should().Be(RustChannel.Nightly);
        file.Components.Should().ContainInOrder("rustfmt", "clippy", "rust-src");
        file.Targets.Should().HaveCount(2);
        file.Profile.Should().Be("minimal");
        file.FilePath.Should().Be("/work/rust-toolchain.toml");
        file.IsLegacyFormat.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_a_legacy_single_line_channel()
    {
        //Act
        var file = RustToolchainFile.Parse("1.80.0\n");

        //Assert
        file.Channel.Should().Be("1.80.0");
        file.IsLegacyFormat.Should().BeTrue();
        file.ChannelKind.Should().Be(RustChannel.Stable);
    }

    [Fact]
    public void Parse_reads_a_custom_toolchain_path()
    {
        //Act
        var file = RustToolchainFile.Parse("[toolchain]\npath = \"/opt/my-rust\"\n");

        //Assert
        file.Channel.Should().BeNull();
        file.ToolchainPath.Should().Be("/opt/my-rust");
    }

    [Theory]
    [InlineData("[package]\nname = \"x\"\n")]
    [InlineData("[toolchain\nchannel = ")]
    [InlineData("   ")]
    public void Parse_throws_RustToolsException_for_unusable_text(string text)
    {
        //Arrange
        Action act = () => RustToolchainFile.Parse(text, "bad.toml");

        //Assert
        act.Should().Throw<RustToolsException>();
    }

    [Fact]
    public void FindAndLoad_walks_up_to_a_parent_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var expected = sandbox.WriteFile("rust-toolchain.toml", "[toolchain]\nchannel = \"1.94.1\"\n");
        var nested = sandbox.CreateDirectory(Path.Combine("crates", "app", "src"));

        //Act
        var file = RustToolchainFile.FindAndLoad(nested);

        //Assert
        file.FilePath.Should().Be(expected);
        file.Channel.Should().Be("1.94.1");
    }

    [Fact]
    public void Find_prefers_the_toml_file_over_the_legacy_file()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("rust-toolchain", "beta\n");
        var expected = sandbox.WriteFile("rust-toolchain.toml", "[toolchain]\nchannel = \"stable\"\n");

        //Act
        var found = RustToolchainFile.Find(sandbox.Directory);

        //Assert
        found.Should().Be(expected);
    }

    [Fact]
    public void Find_returns_null_for_a_missing_folder()
        => RustToolchainFile.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Should().BeNull();

    [Theory]
    [InlineData("stable", RustChannel.Stable)]
    [InlineData("stable-x86_64-pc-windows-msvc", RustChannel.Stable)]
    [InlineData("beta-2026-02-01", RustChannel.Beta)]
    [InlineData("nightly", RustChannel.Nightly)]
    [InlineData("1.94.1", RustChannel.Stable)]
    [InlineData("1.95.0-beta.2", RustChannel.Beta)]
    [InlineData("my-custom-toolchain", RustChannel.Unknown)]
    [InlineData(null, RustChannel.Unknown)]
    public void ChannelOf_classifies_toolchain_names(string name, RustChannel expected)
        => RustToolchainFile.ChannelOf(name).Should().Be(expected);
}
