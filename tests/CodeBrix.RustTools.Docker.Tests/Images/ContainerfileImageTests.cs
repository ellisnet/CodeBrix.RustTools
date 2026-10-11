using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Images;

public class ContainerfileImageTests
{
    private static BuildImageSpec SpecIn(SampleSandbox sandbox)
    {
        var containerfile = sandbox.WriteFile("Containerfile", "FROM busybox:latest\nARG NOTE=x\nCOPY payload.txt /payload.txt\n");
        sandbox.WriteFile("payload.txt", "one");
        sandbox.WriteFile("nested/data.bin", "two");
        return new BuildImageSpec { Containerfile = containerfile, ContainerfileBuildArgs = { ["NOTE"] = "a" } };
    }

    [Fact]
    public void ComputeHash_is_16_hex_digits_and_deterministic()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);

        //Act
        var first = ContainerfileImage.ComputeHash(spec);
        var second = ContainerfileImage.ComputeHash(spec.Clone());

        //Assert
        first.Should().MatchRegex("^[0-9a-f]{16}$");
        second.Should().Be(first);
        ContainerfileImage.TagFor(spec).Should().Be("containerfile-" + first);
    }

    [Fact]
    public void ComputeHash_does_not_depend_on_the_order_of_the_build_arguments()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);
        spec.ContainerfileBuildArgs = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" };
        var reordered = spec.Clone();
        reordered.ContainerfileBuildArgs = new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" };

        //Assert
        ContainerfileImage.ComputeHash(reordered).Should().Be(ContainerfileImage.ComputeHash(spec));
    }

    [Fact]
    public void ComputeHash_changes_with_the_containerfile_a_build_argument_or_a_context_file()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);
        var baseline = ContainerfileImage.ComputeHash(spec);

        //Act
        var otherArgument = spec.Clone();
        otherArgument.ContainerfileBuildArgs["NOTE"] = "b";
        var withArgument = ContainerfileImage.ComputeHash(otherArgument);

        sandbox.WriteFile("nested/data.bin", "changed");
        var withChangedFile = ContainerfileImage.ComputeHash(spec);
        sandbox.WriteFile("nested/data.bin", "two");
        sandbox.WriteFile("new.txt", "added");
        var withAddedFile = ContainerfileImage.ComputeHash(spec);
        File.Delete(Path.Combine(sandbox.Directory, "new.txt"));
        File.AppendAllText(spec.Containerfile, "RUN true\n");
        var withChangedContainerfile = ContainerfileImage.ComputeHash(spec);

        //Assert
        new[] { withArgument, withChangedFile, withAddedFile, withChangedContainerfile }.Should().OnlyHaveUniqueItems();
        new[] { withArgument, withChangedFile, withAddedFile, withChangedContainerfile }.Should().NotContain(baseline);
    }

    [Fact]
    public void ComputeHash_reads_only_the_chosen_context_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);
        spec.ContainerfileContext = Path.Combine(sandbox.Directory, "nested");
        var baseline = ContainerfileImage.ComputeHash(spec);

        //Act
        sandbox.WriteFile("payload.txt", "outside the context");
        var after = ContainerfileImage.ComputeHash(spec);

        //Assert
        after.Should().Be(baseline);
    }

    [Fact]
    public void ComputeHash_rejects_a_spec_without_a_containerfile()
    {
        //Act
        var act = () => ContainerfileImage.ComputeHash(new BuildImageSpec());

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateContainerfile_accepts_a_spec_without_a_containerfile()
    {
        //Act
        var act = () => new BuildImageSpec { BaseImage = "rust", Tag = "1" }.ValidateContainerfile();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateContainerfile_rejects_a_base_image_or_tag_next_to_a_containerfile()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var withBase = SpecIn(sandbox);
        withBase.BaseImage = "rust";
        var withTag = SpecIn(sandbox);
        withTag.Tag = "latest";

        //Assert
        ((Action)withBase.ValidateContainerfile).Should().Throw<ArgumentException>();
        ((Action)withTag.ValidateContainerfile).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateContainerfile_rejects_a_missing_containerfile_or_context()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var missingFile = new BuildImageSpec { Containerfile = Path.Combine(sandbox.Directory, "nope", "Containerfile") };
        var missingContext = SpecIn(sandbox);
        missingContext.ContainerfileContext = Path.Combine(sandbox.Directory, "no-such-folder");

        //Assert
        ((Action)missingFile.ValidateContainerfile).Should().Throw<FileNotFoundException>();
        ((Action)missingContext.ValidateContainerfile).Should().Throw<DirectoryNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    [InlineData("HAS SPACE")]
    public void ValidateContainerfile_rejects_an_invalid_build_argument_name(string name)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);
        spec.ContainerfileBuildArgs[name] = "v";

        //Act
        var act = () => spec.ValidateContainerfile();

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveContainerfileContext_defaults_to_the_containerfile_s_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);

        //Assert
        spec.ResolveContainerfileContext().Should().Be(Path.GetFullPath(sandbox.Directory));
    }

    [Fact]
    public void WithDefaults_leaves_the_base_image_and_tag_unset_for_a_containerfile()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = SpecIn(sandbox);

        //Act
        var resolved = spec.WithDefaults(new DockerRustWorkbenchOptions());

        //Assert
        resolved.BaseImage.Should().BeNull();
        resolved.Tag.Should().BeNull();
        resolved.ImageName.Should().Be(DockerRustWorkbenchOptions.DefaultBuildImageRepository);
        resolved.UsesContainerfile.Should().BeTrue();
    }

    [Fact]
    public void Clone_copies_the_containerfile_settings()
    {
        //Arrange
        var spec = new BuildImageSpec { Containerfile = "/x/Containerfile", ContainerfileContext = "/x", ContainerfileBuildArgs = { ["A"] = "1" } };

        //Act
        var copy = spec.Clone();
        copy.ContainerfileBuildArgs["B"] = "2";

        //Assert
        copy.Containerfile.Should().Be("/x/Containerfile");
        copy.ContainerfileContext.Should().Be("/x");
        spec.ContainerfileBuildArgs.Should().HaveCount(1);
    }
}
