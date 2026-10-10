using System;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Artifacts;

public class ArtifactRetrieverTests
{
    private const string Debug = "/work/target/debug/";

    private static BuildArtifact Library() => new BuildArtifact
    {
        PackageName = "test_project_13",
        TargetName = "test_project_13",
        Kinds = new[] { "cdylib", "rlib" },
        Filenames = new[] { Debug + "libtest_project_13.so", Debug + "libtest_project_13.rlib" },
    };

    private static BuildArtifact Program() => new BuildArtifact
    {
        PackageName = "test_project_13",
        TargetName = "test_project_13",
        Kinds = new[] { "bin" },
        Executable = Debug + "test_project_13",
        Filenames = new[] { Debug + "test_project_13" },
    };

    [Fact]
    public void Select_takes_executables_and_shared_libraries_by_default()
    {
        //Act
        var selection = ArtifactRetriever.Select(new[] { Library(), Program() }, new ArtifactRetrievalOptions());

        //Assert
        selection.Select(s => (s.ContainerPath, s.Kind)).Should().Equal(
            (Debug + "libtest_project_13.so", ArtifactKind.SharedLibrary),
            (Debug + "test_project_13", ArtifactKind.Executable));
    }

    [Fact]
    public void Select_honours_the_kind_switches()
    {
        //Arrange
        var options = new ArtifactRetrievalOptions { IncludeExecutables = false, IncludeSharedLibraries = false, IncludeRlibs = true };

        //Act
        var selection = ArtifactRetriever.Select(new[] { Library(), Program() }, options);

        //Assert
        selection.Should().ContainSingle().Which.ContainerPath.Should().Be(Debug + "libtest_project_13.rlib");
    }

    [Fact]
    public void Select_skips_build_scripts_test_harnesses_and_metadata_files()
    {
        //Arrange
        var buildScript = new BuildArtifact { Kinds = new[] { "custom-build" }, Executable = Debug + "build/x/build-script-build", Filenames = new[] { Debug + "build/x/build-script-build" } };
        var harness = new BuildArtifact { Kinds = new[] { "lib" }, Executable = Debug + "deps/x-123", Filenames = new[] { Debug + "deps/x-123" }, Profile = new ArtifactProfile { Test = true } };
        var rmeta = new BuildArtifact { Kinds = new[] { "lib" }, Filenames = new[] { Debug + "deps/libx.rmeta" } };
        var options = new ArtifactRetrievalOptions { IncludeRlibs = true, IncludeStaticLibraries = true };

        //Act
        var selection = ArtifactRetriever.Select(new[] { buildScript, harness, rmeta }, options);

        //Assert
        selection.Should().BeEmpty();
    }

    [Fact]
    public void Select_skips_proc_macro_crates_and_build_scripts_for_the_host_target()
    {
        //Arrange
        var procMacro = new BuildArtifact { PackageName = "serde_derive", Kinds = new[] { "proc-macro" }, Filenames = new[] { Debug + "deps/libserde_derive-1a2b.so" } };
        var buildScript = new BuildArtifact { Kinds = new[] { "custom-build" }, Executable = Debug + "build/x/build-script-build", Filenames = new[] { Debug + "build/x/build-script-build" } };

        //Act
        var selection = ArtifactRetriever.Select(new[] { Program(), Library(), procMacro, buildScript }, new ArtifactRetrievalOptions(), CrossTarget.LinuxX64);

        //Assert
        selection.Select(s => s.ContainerPath).Should().Equal(
            Debug + "test_project_13",
            Debug + "libtest_project_13.so");
    }

    [Fact]
    public void Select_takes_only_the_target_output_folder_for_a_cross_target()
    {
        //Arrange
        const string cross = "/work/target/x86_64-pc-windows-gnullvm/release/";
        const string host = "/work/target/release/";
        var program = new BuildArtifact { Kinds = new[] { "bin" }, Executable = cross + "my_app.exe", Filenames = new[] { cross + "my_app.exe" } };
        var library = new BuildArtifact { Kinds = new[] { "cdylib" }, Filenames = new[] { cross + "my_lib.dll" } };
        var procMacro = new BuildArtifact { Kinds = new[] { "proc-macro" }, Filenames = new[] { host + "deps/libclap_derive-3c4d.so" } };
        var buildScript = new BuildArtifact { Kinds = new[] { "custom-build" }, Executable = host + "build/x/build-script-build", Filenames = new[] { host + "build/x/build-script-build" } };
        var hostHelper = new BuildArtifact { Kinds = new[] { "lib" }, Filenames = new[] { host + "deps/libhelper-5e6f.rlib", host + "deps/libhelper-5e6f.so" } };
        var options = new ArtifactRetrievalOptions { IncludeRlibs = true };

        //Act
        var selection = ArtifactRetriever.Select(new[] { program, library, procMacro, buildScript, hostHelper }, options, CrossTarget.WindowsX64Gnullvm);

        //Assert
        selection.Select(s => (s.ContainerPath, s.Kind)).Should().Equal(
            (cross + "my_app.exe", ArtifactKind.Executable),
            (cross + "my_lib.dll", ArtifactKind.SharedLibrary));
    }

    [Fact]
    public void Select_takes_a_path_reported_twice_once()
        => ArtifactRetriever.Select(new[] { Program(), Program() }, new ArtifactRetrievalOptions()).Should().ContainSingle();

    [Theory]
    [InlineData("libx.so", ArtifactKind.SharedLibrary)]
    [InlineData("x.dll", ArtifactKind.SharedLibrary)]
    [InlineData("libx.dylib", ArtifactKind.SharedLibrary)]
    [InlineData("libx.a", ArtifactKind.StaticLibrary)]
    [InlineData("libx.dll.a", ArtifactKind.StaticLibrary)]
    [InlineData("x.lib", ArtifactKind.StaticLibrary)]
    [InlineData("libx.rlib", ArtifactKind.Rlib)]
    [InlineData("x.exe", ArtifactKind.Executable)]
    [InlineData("libx.rmeta", ArtifactKind.Other)]
    [InlineData("x.d", ArtifactKind.Other)]
    public void Classify_names_the_kind_from_the_file_name(string fileName, ArtifactKind expected)
        => ArtifactRetriever.Classify(Debug + fileName, new BuildArtifact()).Should().Be(expected);

    [Fact]
    public void Classify_treats_the_artifact_executable_as_an_executable()
        => ArtifactRetriever.Classify(Debug + "test_project_13", Program()).Should().Be(ArtifactKind.Executable);

    [Theory]
    [InlineData(ArtifactLayout.Flat, CrossTarget.LinuxX64, "")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.LinuxX64, "x86_64-unknown-linux-gnu")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.WindowsX64Gnullvm, "x86_64-pc-windows-gnullvm")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.LinuxX64, "runtimes/linux-x64/native")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.WindowsX64Gnullvm, "runtimes/win-x64/native")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.WindowsArm64Gnullvm, "aarch64-pc-windows-gnullvm")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.WindowsArm64Gnullvm, "runtimes/win-arm64/native")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.AndroidArm64, "runtimes/android-arm64/native")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.AndroidX64, "runtimes/android-x64/native")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.MacOSArm64, "aarch64-apple-darwin")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.MacOSX64, "runtimes/osx-x64/native")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.MacOSUniversal, "universal-apple-darwin")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.MacOSUniversal, "runtimes/osx/native")]
    public void RelativeDirectory_follows_the_layout(ArtifactLayout layout, CrossTarget target, string expected)
        => ArtifactRetriever.RelativeDirectory(layout, target).Should().Be(expected);

    [Theory]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.LinuxArm64, "runtimes/linux-arm64/native")]
    [InlineData(ArtifactLayout.ByTarget, CrossTarget.LinuxRiscV64, "riscv64gc-unknown-linux-gnu")]
    [InlineData(ArtifactLayout.NuGetRuntimes, CrossTarget.LinuxRiscV64, "runtimes/linux-riscv64/native")]
    public void RelativeDirectory_names_the_linux_targets_after_their_own_triple_and_rid(ArtifactLayout layout, CrossTarget target, string expected)
        => ArtifactRetriever.RelativeDirectory(layout, target).Should().Be(expected);

    [Fact]
    public void RelativeDirectory_refuses_the_unresolved_native_target()
    {
        //Act
        var act = () => ArtifactRetriever.RelativeDirectory(ArtifactLayout.ByTarget, CrossTarget.LinuxNative);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*LinuxNative*");
    }

    [Fact]
    public void Select_treats_a_linux_target_as_native_only_when_it_is_the_engine_s_own()
    {
        //Arrange
        const string cross = "/work/target/aarch64-unknown-linux-gnu/release/";
        const string host = "/work/target/release/";
        var artifacts = new[]
        {
            new BuildArtifact { Kinds = new[] { "cdylib" }, Filenames = new[] { cross + "libtest_project_13.so" } },
            new BuildArtifact { Kinds = new[] { "cdylib" }, Filenames = new[] { host + "libhelper.so" } },
        };

        //Act
        var onX64Engine = ArtifactRetriever.Select(artifacts, new ArtifactRetrievalOptions(), CrossTarget.LinuxArm64, "x86_64-unknown-linux-gnu");
        var onArm64Engine = ArtifactRetriever.Select(artifacts, new ArtifactRetrievalOptions(), CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu");

        //Assert
        onX64Engine.Select(s => s.ContainerPath).Should().Equal(cross + "libtest_project_13.so");
        onArm64Engine.Select(s => s.ContainerPath).Should().Equal(cross + "libtest_project_13.so", host + "libhelper.so");
    }

    [Fact]
    public void Validate_requires_a_host_directory()
    {
        //Act
        var act = () => ArtifactRetriever.Validate(new ArtifactRetrievalOptions());

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*HostDirectory*");
    }

    [Fact]
    public void Validate_rejects_null_options()
    {
        //Act
        var act = () => ArtifactRetriever.Validate(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
