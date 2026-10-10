using System;
using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class ContainerCommandRunnerTests
{
    [Fact]
    public void ValidateCommand_rejects_null()
    {
        //Act
        var act = () => ContainerCommandRunner.ValidateCommand(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateCommand_rejects_an_empty_command()
    {
        //Act
        var act = () => ContainerCommandRunner.ValidateCommand(Array.Empty<string>());

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateCommand_rejects_a_null_argument()
    {
        //Act
        var act = () => ContainerCommandRunner.ValidateCommand(new[] { "cargo", null });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateCommand_accepts_a_program_with_arguments()
    {
        //Act
        var act = () => ContainerCommandRunner.ValidateCommand(new[] { "cargo", "--version" });

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ContainerCommandResult_succeeds_only_with_exit_code_zero()
    {
        //Assert
        new ContainerCommandResult { ExitCode = 0 }.Succeeded.Should().BeTrue();
        new ContainerCommandResult { ExitCode = 101 }.Succeeded.Should().BeFalse();
    }
}
