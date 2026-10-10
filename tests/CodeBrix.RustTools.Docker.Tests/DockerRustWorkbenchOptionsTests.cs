using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests;

public class DockerRustWorkbenchOptionsTests
{
    [Fact]
    public void defaults_are_the_documented_values()
    {
        //Act
        var options = new DockerRustWorkbenchOptions();

        //Assert
        options.ClientOptions.Should().BeNull();
        options.Endpoint.Should().BeNull();
        options.DefaultImage.Should().Be("rust");
        options.DefaultTag.Should().Be("latest");
        options.LabelPrefix.Should().Be("codebrix.rusttools.docker");
        options.CacheVolumeName.Should().Be("codebrix-rusttools-cargo-cache");
        options.BuildImageRepository.Should().Be("codebrix-rusttools-docker");
        options.ContainerNamePrefix.Should().Be("codebrix-rusttools");
        options.KeepContainerByDefault.Should().BeFalse();
        options.DefaultTimeouts.Should().NotBeNull();
    }

    [Fact]
    public void DefaultExcludePatterns_are_git_target_node_modules_and_ds_store()
        => new DockerRustWorkbenchOptions().DefaultExcludePatterns.Should().Equal(".git", "target", "node_modules", ".DS_Store");

    [Fact]
    public void StandardExcludePatterns_match_the_default_list()
        => DockerRustWorkbenchOptions.StandardExcludePatterns.Should().Equal(new DockerRustWorkbenchOptions().DefaultExcludePatterns);

    [Fact]
    public void DefaultExcludePatterns_are_a_fresh_list_per_instance()
    {
        //Arrange
        var first = new DockerRustWorkbenchOptions();

        //Act
        first.DefaultExcludePatterns.Add("*.tmp");

        //Assert
        new DockerRustWorkbenchOptions().DefaultExcludePatterns.Should().NotContain("*.tmp");
    }

    [Fact]
    public void DefaultTimeouts_have_the_documented_values()
    {
        //Act
        var timeouts = new DockerRustTimeouts();

        //Assert
        timeouts.ApiCall.Should().Be(TimeSpan.FromSeconds(100));
        timeouts.ImagePreparation.Should().Be(TimeSpan.FromMinutes(60));
        timeouts.Command.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Clone_is_deep()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions
        {
            ClientOptions = new DockerClientOptions { Endpoint = "unix:///tmp/a.sock", SshArguments = new List<string> { "-i", "k" } },
            Endpoint = "unix:///tmp/b.sock",
            KeepContainerByDefault = true,
        };

        //Act
        var copy = options.Clone();
        copy.ClientOptions.SshArguments.Add("-v");
        copy.DefaultExcludePatterns.Add("dist");
        copy.DefaultTimeouts.Command = TimeSpan.FromSeconds(1);

        //Assert
        copy.Endpoint.Should().Be("unix:///tmp/b.sock");
        copy.KeepContainerByDefault.Should().BeTrue();
        copy.ClientOptions.Should().NotBeSameAs(options.ClientOptions);
        options.ClientOptions.SshArguments.Should().Equal("-i", "k");
        options.DefaultExcludePatterns.Should().NotContain("dist");
        options.DefaultTimeouts.Command.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void CreateClientOptions_uses_the_endpoint_and_api_timeout()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { Endpoint = " unix:///tmp/docker.sock ", DefaultTimeouts = { ApiCall = TimeSpan.FromSeconds(7) } };

        //Act
        var client = options.CreateClientOptions();

        //Assert
        client.Endpoint.Should().Be("unix:///tmp/docker.sock");
        client.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void CreateClientOptions_copies_client_options_and_lets_endpoint_override()
    {
        //Arrange
        var supplied = new DockerClientOptions { Endpoint = "unix:///tmp/a.sock", DockerCliPath = "/opt/docker", DefaultTimeout = TimeSpan.FromSeconds(3) };
        var options = new DockerRustWorkbenchOptions { ClientOptions = supplied, Endpoint = "tcp://build-host:2375" };

        //Act
        var client = options.CreateClientOptions();

        //Assert
        client.Should().NotBeSameAs(supplied);
        client.Endpoint.Should().Be("tcp://build-host:2375");
        client.DockerCliPath.Should().Be("/opt/docker");
        client.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(3));
        supplied.Endpoint.Should().Be("unix:///tmp/a.sock");
    }

    [Fact]
    public void Validate_rejects_a_blank_label_prefix()
    {
        //Act
        var act = () => new DockerRustWorkbenchOptions { LabelPrefix = " " }.Validate();

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildImageRepository_is_the_default_for_the_default_label_prefix()
    {
        //Act
        var options = new DockerRustWorkbenchOptions();

        //Assert
        options.BuildImageRepository.Should().Be(DockerRustWorkbenchOptions.DefaultBuildImageRepository);
        options.IsBuildImageRepositorySet.Should().BeFalse();
    }

    [Fact]
    public void BuildImageRepository_follows_a_non_default_label_prefix()
    {
        //Act
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "codebrix.rusttools.scratch" };

        //Assert
        options.BuildImageRepository.Should().Be("codebrix-rusttools-scratch");
        options.IsBuildImageRepositorySet.Should().BeFalse();
    }

    [Fact]
    public void BuildImageRepository_differs_between_workbenches_with_different_label_prefixes()
    {
        //Arrange
        var first = new DockerRustWorkbenchOptions();
        var second = new DockerRustWorkbenchOptions { LabelPrefix = "codebrix.rusttools.scratch" };

        //Act
        var spec = new BuildImageSpec { InstallClippyRustfmt = true };
        var firstReference = DockerfileGenerator.DerivedImageReference(spec.WithDefaults(first));
        var secondReference = DockerfileGenerator.DerivedImageReference(spec.WithDefaults(second));

        //Assert
        firstReference.Should().StartWith("codebrix-rusttools-docker:");
        secondReference.Should().StartWith("codebrix-rusttools-scratch:");
        firstReference.Should().NotBe(secondReference);
    }

    [Fact]
    public void BuildImageRepository_set_explicitly_wins_over_the_label_prefix()
    {
        //Act
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "codebrix.rusttools.scratch", BuildImageRepository = "my-images" };

        //Assert
        options.BuildImageRepository.Should().Be("my-images");
        options.IsBuildImageRepositorySet.Should().BeTrue();
    }

    [Fact]
    public void BuildImageRepository_set_to_the_default_explicitly_stays_with_another_label_prefix()
    {
        //Act
        var options = new DockerRustWorkbenchOptions
        {
            BuildImageRepository = DockerRustWorkbenchOptions.DefaultBuildImageRepository,
            LabelPrefix = "codebrix.rusttools.scratch",
        };

        //Assert
        options.BuildImageRepository.Should().Be("codebrix-rusttools-docker");
        options.IsBuildImageRepositorySet.Should().BeTrue();
    }

    [Fact]
    public void BuildImageRepository_set_to_null_follows_the_label_prefix_again()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "team.builds", BuildImageRepository = "my-images" };

        //Act
        options.BuildImageRepository = null;

        //Assert
        options.BuildImageRepository.Should().Be("team-builds");
        options.IsBuildImageRepositorySet.Should().BeFalse();
    }

    [Theory]
    [InlineData("codebrix.rusttools.docker", "codebrix-rusttools-docker")]
    [InlineData("codebrix.rusttools.scratch", "codebrix-rusttools-scratch")]
    [InlineData("  My.Team_Builds  ", "my-team-builds")]
    [InlineData("com.example/ci..builds-", "com-example-ci-builds")]
    [InlineData(".leading.and.trailing.", "leading-and-trailing")]
    [InlineData("a", "a")]
    [InlineData("...", "")]
    [InlineData(" ", "")]
    [InlineData(null, "")]
    public void RepositoryFromLabelPrefix_gives_a_valid_lower_case_repository_name(string prefix, string expected)
        => DockerRustWorkbenchOptions.RepositoryFromLabelPrefix(prefix).Should().Be(expected);

    [Theory]
    [InlineData("codebrix.rusttools.scratch")]
    [InlineData("Weird..Prefix__With~Stuff")]
    [InlineData("x")]
    public void RepositoryFromLabelPrefix_is_accepted_as_an_image_repository(string prefix)
    {
        //Arrange
        var spec = new BuildImageSpec { InstallClippyRustfmt = true };

        //Act
        var reference = DockerfileGenerator.DerivedImageReference(spec, DockerRustWorkbenchOptions.RepositoryFromLabelPrefix(prefix));

        //Assert
        reference.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*:[0-9a-f]{16}$");
    }

    [Fact]
    public void Clone_keeps_a_derived_repository_derived()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "team.builds" };

        //Act
        var copy = options.Clone();
        copy.LabelPrefix = "other.builds";

        //Assert
        copy.IsBuildImageRepositorySet.Should().BeFalse();
        copy.BuildImageRepository.Should().Be("other-builds");
        options.BuildImageRepository.Should().Be("team-builds");
    }

    [Fact]
    public void Clone_keeps_an_explicit_repository_explicit()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "team.builds", BuildImageRepository = "my-images" };

        //Act
        var copy = options.Clone();
        copy.LabelPrefix = "other.builds";

        //Assert
        copy.IsBuildImageRepositorySet.Should().BeTrue();
        copy.BuildImageRepository.Should().Be("my-images");
    }

    [Fact]
    public void Validate_accepts_a_derived_repository()
    {
        //Act
        var act = () => new DockerRustWorkbenchOptions { LabelPrefix = "codebrix.rusttools.scratch" }.Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_rejects_a_label_prefix_that_gives_no_repository_name()
    {
        //Act
        var act = () => new DockerRustWorkbenchOptions { LabelPrefix = "..." }.Validate();

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*set BuildImageRepository explicitly*");
    }

    [Fact]
    public void Validate_accepts_an_explicit_repository_for_a_label_prefix_that_gives_no_name()
    {
        //Act
        var act = () => new DockerRustWorkbenchOptions { LabelPrefix = "...", BuildImageRepository = "my-images" }.Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_rejects_a_blank_explicit_repository()
    {
        //Act
        var act = () => new DockerRustWorkbenchOptions { BuildImageRepository = " " }.Validate();

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
