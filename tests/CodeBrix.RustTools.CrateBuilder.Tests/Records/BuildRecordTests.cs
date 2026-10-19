using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Records;

public class BuildRecordTests
{
    private static BuildRecord Sample() => new BuildRecord
    {
        CrateBuilderVersion = "1.0.0.0",
        StartedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        FinishedUtc = new DateTimeOffset(2026, 1, 2, 3, 5, 6, TimeSpan.Zero),
        Source = new CrateSourceIdentity
        {
            Kind = CrateSourceIdentity.GitKind,
            Url = "https://example.com/a.git",
            Commit = new string('a', 40),
            ManifestPath = "Cargo.toml",
            CrateName = "my-crate",
            CrateVersion = "0.1.0",
            CrateTypes = new List<string> { "cdylib", "rlib" },
        },
        Mode = BuildMode.Docker,
        Profile = CrateBuildProfile.Release,
        Features = new List<string> { "json" },
        Locked = true,
        ToolchainDescription = "Docker engine 29 (x86_64)",
        Targets = new List<TargetRecord>
        {
            new TargetRecord
            {
                Rid = "osx-arm64",
                Triple = "aarch64-apple-darwin",
                Outcome = CrateTargetOutcome.Compiled,
                Provenance = ArtifactProvenance.DevelopmentBuild,
                Artifacts = new List<ArtifactRecord> { new ArtifactRecord { Path = "runtimes/osx-arm64/native/libmy_crate.dylib", Sha256 = new string('b', 64), Length = 42 } },
            },
            new TargetRecord { Rid = "android-x64", Triple = "x86_64-linux-android", Outcome = CrateTargetOutcome.NotBuildableInThisMode, Message = "needs an x86-64 engine" },
        },
    };

    [Fact]
    public void The_source_says_whether_a_clone_was_reused_and_false_is_written_too()
    {
        //Arrange
        var reused = Sample();
        reused.Source.CloneReused = true;

        //Act
        var reusedJson = reused.ToJson();
        var freshJson = Sample().ToJson();
        var loaded = System.Text.Json.JsonSerializer.Deserialize<CrateSourceIdentity>(System.Text.Json.JsonDocument.Parse(reusedJson).RootElement.GetProperty("source").GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        //Assert
        reusedJson.Should().Contain("\"cloneReused\": true");
        freshJson.Should().Contain("\"cloneReused\": false", "the field is always written, so a reader never has to guess");
        loaded.CloneReused.Should().BeTrue();
        new CrateSourceIdentity().CloneReused.Should().BeFalse();
    }

    [Fact]
    public void Save_and_Load_round_trip()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = Path.Combine(sandbox.Directory, "out", BuildRecord.DefaultFileName);

        //Act
        Sample().Save(path);
        var loaded = BuildRecord.Load(path);

        //Assert
        loaded.CrateBuilderVersion.Should().Be("1.0.0.0");
        loaded.StartedUtc.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        loaded.Source.Commit.Should().Be(new string('a', 40));
        loaded.Source.CrateTypes.Should().Equal("cdylib", "rlib");
        loaded.Mode.Should().Be(BuildMode.Docker);
        loaded.Features.Should().Equal("json");
        loaded.Locked.Should().BeTrue();
        loaded.Targets.Should().HaveCount(2);
        loaded.Targets[0].Provenance.Should().Be(ArtifactProvenance.DevelopmentBuild);
        loaded.Targets[0].Artifacts[0].Length.Should().Be(42);
        loaded.Targets[1].Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        loaded.Wrapper.Should().BeNull();
        loaded.WebGpuNativeVersion.Should().BeNull();
    }

    [Fact]
    public void the_json_is_indented_camel_case_with_enums_as_strings_and_no_nulls()
    {
        //Act
        var json = Sample().ToJson();

        //Assert
        json.Should().Contain("\n  \"crateBuilderVersion\": \"1.0.0.0\"");
        json.Should().Contain("\"mode\": \"Docker\"");
        json.Should().Contain("\"outcome\": \"NotBuildableInThisMode\"");
        json.Should().Contain("\"provenance\": \"DevelopmentBuild\"");
        json.Should().NotContain("null");
        json.Should().NotContain("webGpuNativeVersion");
        json.Should().NotContain("\"wrapper\"");
        json.Should().NotContain("\r");
        json.Should().EndWith("}\n");
    }

    [Fact]
    public void Load_reports_invalid_json_as_a_crate_builder_exception()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile("bad.json", "{ not json");

        //Act
        var act = () => BuildRecord.Load(path);

        //Assert
        act.Should().Throw<CrateBuilderException>().Which.Stage.Should().Be(CrateBuildStage.Recording);
    }

    [Fact]
    public void Load_requires_an_existing_file()
    {
        //Act
        var act = () => BuildRecord.Load(Path.Combine(AppContext.BaseDirectory, "no-such-record.json"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }
}
