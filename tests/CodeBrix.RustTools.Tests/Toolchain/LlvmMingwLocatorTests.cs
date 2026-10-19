using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class LlvmMingwLocatorTests
{
    private const string X64 = WindowsGnullvmLinkProfile.X64Triple;
    private const string Arm64 = WindowsGnullvmLinkProfile.Arm64Triple;

    private static string FakeLlvmMingw(SampleSandbox sandbox, string folder, params string[] machines)
    {
        var root = sandbox.CreateDirectory(folder);
        foreach (var machine in machines)
        {
            sandbox.WriteFile(Path.Combine(folder, "bin", machine + "-w64-mingw32-clang"), "#!/bin/sh\n");
        }

        return root;
    }

    private static RustToolchainInfo Toolchain(params string[] installedTargets) => new RustToolchainInfo
    {
        OperatingSystem = OperatingSystemKind.DebianLinux,
        Tools = TestToolchains.CompleteTools(),
        HostTriple = "x86_64-unknown-linux-gnu",
        ActiveToolchain = "stable-x86_64-unknown-linux-gnu",
        InstalledToolchains = new[] { "stable-x86_64-unknown-linux-gnu" },
        InstalledTargets = new[] { "x86_64-unknown-linux-gnu" }.Concat(installedTargets).ToList(),
    };

    [Fact]
    public void FindBinDirectory_takes_an_explicit_folder_or_its_bin_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeLlvmMingw(sandbox, "llvm-mingw", "x86_64");

        //Act
        var fromRoot = LlvmMingwLocator.FindBinDirectory(X64, root, _ => null, File.Exists, false);
        var fromBin = LlvmMingwLocator.FindBinDirectory(X64, Path.Combine(root, "bin"), _ => null, File.Exists, false);

        //Assert
        fromRoot.Should().Be(Path.Combine(root, "bin"));
        fromBin.Should().Be(Path.Combine(root, "bin"));
    }

    [Fact]
    public void FindBinDirectory_does_not_fall_back_when_the_explicit_folder_lacks_the_target()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var explicitRoot = FakeLlvmMingw(sandbox, "explicit", "x86_64");
        var home = FakeLlvmMingw(sandbox, "home", "x86_64", "aarch64");

        //Act
        var found = LlvmMingwLocator.FindBinDirectory(Arm64, explicitRoot, name => name == LlvmMingwLocator.HomeVariable ? home : null, File.Exists, false);

        //Assert
        found.Should().BeNull();
    }

    [Fact]
    public void FindBinDirectory_searches_LLVM_MINGW_HOME_then_PATH()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var home = FakeLlvmMingw(sandbox, "home", "x86_64");
        var onPath = FakeLlvmMingw(sandbox, "onpath", "aarch64");
        var environment = new Dictionary<string, string>
        {
            [LlvmMingwLocator.HomeVariable] = home,
            ["PATH"] = string.Join(Path.PathSeparator, sandbox.CreateDirectory("empty"), Path.Combine(onPath, "bin")),
        };
        string Read(string name) => environment.TryGetValue(name, out var value) ? value : null;

        //Act
        var x64 = LlvmMingwLocator.FindBinDirectory(X64, null, Read, File.Exists, false);
        var arm64 = LlvmMingwLocator.FindBinDirectory(Arm64, null, Read, File.Exists, false);
        var none = LlvmMingwLocator.FindBinDirectory(X64, null, _ => null, File.Exists, false);

        //Assert
        x64.Should().Be(Path.Combine(home, "bin"));
        arm64.Should().Be(Path.Combine(onPath, "bin"));
        none.Should().BeNull();
    }

    [Fact]
    public void FindBinDirectory_adds_exe_for_a_windows_host()
    {
        //Arrange
        var asked = new List<string>();

        //Act
        LlvmMingwLocator.FindBinDirectory(X64, "/opt/llvm-mingw", _ => null, path => { asked.Add(path); return false; }, true);

        //Assert
        asked.Should().Equal("/opt/llvm-mingw/bin/x86_64-w64-mingw32-clang.exe", "/opt/llvm-mingw/x86_64-w64-mingw32-clang.exe");
    }

    [Fact]
    public void FindBinDirectory_rejects_a_triple_that_is_not_windows_gnullvm()
    {
        //Arrange
        Action act = () => LlvmMingwLocator.FindBinDirectory("x86_64-unknown-linux-gnu", "/opt/llvm-mingw");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForWindowsGnullvm_requires_the_std_and_the_clang_wrapper()
    {
        //Act
        var requirements = ToolchainRequirements.ForWindowsGnullvm(new[] { X64, " AARCH64-pc-windows-gnullvm " }, "/opt/llvm-mingw");

        //Assert
        requirements.RequiredTargets.Should().Equal(X64, Arm64);
        requirements.RequiredWindowsGnullvm.Keys.Should().BeEquivalentTo(new[] { X64, Arm64 });
        requirements.RequiredWindowsGnullvm[X64].Should().Be("/opt/llvm-mingw");
        ToolchainRequirements.ForWindowsGnullvm(new[] { X64 }).RequiredWindowsGnullvm[X64].Should().BeNull();
        Action act = () => ToolchainRequirements.ForWindowsGnullvm(new[] { "x86_64-pc-windows-msvc" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Evaluate_is_ready_when_llvm_mingw_holds_the_targets_clang()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = FakeLlvmMingw(sandbox, "llvm-mingw", "x86_64");

        //Act
        var readiness = ToolchainReadiness.Evaluate(Toolchain(X64), ToolchainRequirements.ForWindowsGnullvm(new[] { X64 }, root));

        //Assert
        readiness.IsReady.Should().BeTrue(readiness.ToString());
        readiness.MissingLlvmMingwTargets.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_blocks_with_LlvmMingwMissing_and_guidance_when_the_clang_is_absent()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var empty = sandbox.CreateDirectory("no-llvm-mingw");

        //Act
        var readiness = ToolchainReadiness.Evaluate(Toolchain(X64), ToolchainRequirements.ForWindowsGnullvm(new[] { X64, Arm64 }, empty));
        var guidance = InstallGuidanceProvider.ForReadiness(readiness);

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.MissingLlvmMingwTargets.Should().Equal(X64, Arm64);
        readiness.MissingTargets.Should().Equal(Arm64);
        readiness.Issues.Should().Contain(i => i.Kind == ReadinessIssueKind.LlvmMingwMissing && i.IsBlocking && i.Subject == X64 && i.Message.Contains("x86_64-w64-mingw32-clang"));
        var llvm = guidance.Single(g => g.Topic == InstallGuidanceTopic.InstallLlvmMingw);
        llvm.Commands[0].Should().Be("rustup target add " + X64 + " " + Arm64);
        llvm.Explanation.Should().Contain(LlvmMingwLocator.HomeVariable);
        llvm.DocumentationUrl.Should().Be(LlvmMingwLocator.ReleasesUrl);
    }

    [Theory]
    [InlineData(OperatingSystemKind.Windows, "setx LLVM_MINGW_HOME")]
    [InlineData(OperatingSystemKind.MacOS, "export LLVM_MINGW_HOME")]
    [InlineData(OperatingSystemKind.DebianLinux, "export LLVM_MINGW_HOME")]
    public void GetGuidance_for_llvm_mingw_names_the_variable_per_operating_system(OperatingSystemKind os, string expected)
    {
        //Act
        var guidance = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallLlvmMingw, os, new[] { "clippy" });

        //Assert
        guidance.Commands.Should().Contain(c => c.StartsWith(expected, StringComparison.Ordinal));
        guidance.Commands[0].Should().Be("rustup target add " + X64 + " " + Arm64);
        guidance.RequiresNewShell.Should().BeTrue();
    }
}
