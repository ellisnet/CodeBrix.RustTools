using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

public class ManifestPathRewriterTests
{
    private static KeyValuePair<IReadOnlyList<string>, string> Change(string newPath, params string[] keys)
        => new KeyValuePair<IReadOnlyList<string>, string>(keys, newPath);

    private const string Manifest = """
        # The crate's manifest.
        [package]
        name = "demo"   # name comment
        version = "0.1.0"

        [dependencies]
        inline = { path = "../a", version = "1" }  # inline comment
        dotted.path = "../b"
        plain = "1.0"

        [dependencies.header]
        # comment above the path
        path = '../c'
        features = ["x"]

        [target.'cfg(unix)'.dependencies]
        unixonly = { path = "../d" }

        [workspace.dependencies]
        shared = { path = "../e" }
        """;

    [Fact]
    public void Rewrite_changes_only_the_path_tokens_in_every_spelling()
    {
        //Arrange
        var changes = new[]
        {
            Change("v/1-a", "dependencies", "inline"),
            Change("v/2-b", "dependencies", "dotted"),
            Change("v/3-c", "dependencies", "header"),
            Change("v/4-d", "target", "cfg(unix)", "dependencies", "unixonly"),
            Change("v/5-e", "workspace", "dependencies", "shared"),
        };

        //Act
        var result = ManifestPathRewriter.Rewrite(Manifest, changes, "Cargo.toml");

        //Assert
        var expected = Manifest
            .Replace("\"../a\"", "\"v/1-a\"").Replace("\"../b\"", "\"v/2-b\"").Replace("'../c'", "'v/3-c'")
            .Replace("\"../d\"", "\"v/4-d\"").Replace("\"../e\"", "\"v/5-e\"");
        result.Should().Be(expected);
        var table = TomlSerializer.Deserialize<TomlTable>(result);
        ((TomlTable)((TomlTable)table["dependencies"])["inline"])["path"].Should().Be("v/1-a");
    }

    [Fact]
    public void Rewrite_quotes_a_value_a_literal_string_cannot_hold()
    {
        //Arrange
        var changes = new[] { Change("it's/here", "dependencies", "header") };

        //Act
        var result = ManifestPathRewriter.Rewrite(Manifest, changes, "Cargo.toml");

        //Assert
        result.Should().Contain("path = \"it's/here\"");
    }

    [Fact]
    public void Rewrite_fails_when_the_entry_has_no_path_value()
    {
        //Arrange
        Action act = () => ManifestPathRewriter.Rewrite(Manifest, new[] { Change("x", "dependencies", "plain") }, "Cargo.toml");

        //Act + Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*dependencies.plain*not found*");
    }

    [Theory]
    [InlineData("plain/path", "\"plain/path\"")]
    [InlineData("quote\"back\\slash", "\"quote\\\"back\\\\slash\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    public void BasicString_escapes_what_toml_requires(string value, string expected) => ManifestPathRewriter.BasicString(value).Should().Be(expected);
}
