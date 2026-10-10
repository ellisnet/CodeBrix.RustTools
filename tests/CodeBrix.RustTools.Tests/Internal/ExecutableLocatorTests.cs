using System;
using System.IO;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Internal;

public class ExecutableLocatorTests
{
    private static string MakeExecutable(SampleSandbox sandbox, string relativePath)
    {
        var path = sandbox.WriteFile(relativePath, "#!/bin/sh\necho fake\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    [Fact]
    public void Locate_finds_executable_on_path()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var expected = MakeExecutable(sandbox, Path.Combine("bin1", "cargo"));
        var locator = new ExecutableLocator(new RustWorkbenchOptions(), Path.Combine(sandbox.Directory, "missing") + ":" + Path.Combine(sandbox.Directory, "bin1"), null, null, isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Cargo);

        //Assert
        found.Should().Be(expected);
    }

    [Fact]
    public void Locate_falls_back_to_cargo_home_bin()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var expected = MakeExecutable(sandbox, Path.Combine("cargohome", "bin", "rustc"));
        var locator = new ExecutableLocator(new RustWorkbenchOptions(), string.Empty, null, Path.Combine(sandbox.Directory, "cargohome"), isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Rustc);

        //Assert
        found.Should().Be(expected);
        locator.CargoHome.Should().Be(Path.Combine(sandbox.Directory, "cargohome"));
    }

    [Fact]
    public void Locate_falls_back_to_dot_cargo_bin_in_home_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var expected = MakeExecutable(sandbox, Path.Combine(".cargo", "bin", "rustup"));
        var locator = new ExecutableLocator(new RustWorkbenchOptions(), string.Empty, sandbox.Directory, null, isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Rustup);

        //Assert
        found.Should().Be(expected);
    }

    [Fact]
    public void Locate_ignores_files_without_execute_permission()
    {
        //Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
        }

        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile(Path.Combine("bin", "cargo"), "not executable");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var locator = new ExecutableLocator(new RustWorkbenchOptions(), Path.Combine(sandbox.Directory, "bin"), null, null, isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Cargo);

        //Assert
        found.Should().BeNull();
    }

    [Fact]
    public void Locate_adds_exe_suffix_on_windows()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var expected = sandbox.WriteFile(Path.Combine("bin", "cargo.exe"), "MZ");
        var locator = new ExecutableLocator(new RustWorkbenchOptions(), Path.Combine(sandbox.Directory, "bin"), null, null, isWindows: true);

        //Act
        var found = locator.Locate(RustTool.Cargo);

        //Assert
        found.Should().Be(expected);
        locator.GetExecutableName(RustTool.Linker).Should().Be("link");
        locator.GetExecutableName(RustTool.CCompiler).Should().Be("cl");
    }

    [Fact]
    public void Locate_prefers_the_options_override_path()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        MakeExecutable(sandbox, Path.Combine("bin", "cargo"));
        var custom = MakeExecutable(sandbox, Path.Combine("custom", "my-cargo"));
        var options = new RustWorkbenchOptions { CargoPath = custom };
        var locator = new ExecutableLocator(options, Path.Combine(sandbox.Directory, "bin"), null, null, isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Cargo);

        //Assert
        found.Should().Be(custom);
    }

    [Fact]
    public void Locate_returns_null_when_the_override_path_does_not_exist()
    {
        //Arrange
        var options = new RustWorkbenchOptions { RustcPath = "/definitely/not/here/rustc" };
        var locator = new ExecutableLocator(options, "/usr/bin", null, null, isWindows: false);

        //Act
        var found = locator.Locate(RustTool.Rustc);

        //Assert
        found.Should().BeNull();
    }

    [Fact]
    public void GetSearchDirectories_orders_additional_then_path_then_cargo_folders()
    {
        //Arrange
        var options = new RustWorkbenchOptions { CargoHome = "/opt/cargo" };
        options.AdditionalSearchDirectories.Add("/extra");
        var locator = new ExecutableLocator(options, "/usr/bin:/extra:/bin", "/home/someone", "/ignored-because-option-wins", isWindows: false);

        //Act
        var directories = locator.GetSearchDirectories();

        //Assert
        directories.Should().ContainInOrder("/extra", "/usr/bin", "/bin", Path.Combine("/opt/cargo", "bin"), Path.Combine("/home/someone", ".cargo", "bin"));
        directories.Should().HaveCount(5);
    }

    [Fact]
    public void Locate_finds_the_real_cargo_when_installed()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var locator = new ExecutableLocator(new RustWorkbenchOptions());

        //Act
        var found = locator.Locate(RustTool.Cargo);

        //Assert
        File.Exists(found).Should().BeTrue();
    }
}
