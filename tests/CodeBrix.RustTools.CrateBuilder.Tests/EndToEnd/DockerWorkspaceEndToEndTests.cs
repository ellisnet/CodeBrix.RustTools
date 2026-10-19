using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// End-to-end Docker-mode build of a workspace MEMBER through a generated facade: the
/// workspace root carries a <c>[patch.crates-io]</c> table (a path patch), a
/// <c>.cargo/config.toml</c> (an <c>[env]</c> value the crate reads with <c>env!</c>) and
/// <c>[profile.release]</c> settings (debug assertions on, a per-package override, and
/// <c>panic = "abort"</c>, which the facade must replace with unwinding). The crate is built
/// offline for linux-x64 and win-x64 in the rust image, wrapped, and a console app checks that
/// every one of those settings reached the native code. Isolated workbench:
/// <see cref="DockerWorkspaceEndToEndFixture"/>. Skips by name without a usable x86-64 engine.
/// </summary>
[Collection("Docker")]
public class DockerWorkspaceEndToEndTests : IClassFixture<DockerWorkspaceEndToEndFixture>
{
    private const string MemberLib = """
        pub fn answer() -> i32 {
            wskit_patched_dep::answer()
        }

        pub fn greeting() -> String {
            env!("WSKIT_GREETING").to_string()
        }

        pub fn checks_enabled() -> bool {
            cfg!(debug_assertions)
        }

        pub fn dependency_checks_enabled() -> bool {
            wskit_patched_dep::checks_enabled()
        }

        pub fn halve(n: i32) -> i32 {
            if n % 2 != 0 {
                panic!("odd input");
            }
            n / 2
        }
        """;

    private const string Program = """
        using System;
        using E2E.Workspace;

        Console.WriteLine(Wskit.Answer());
        Console.WriteLine(Wskit.Greeting());
        Console.WriteLine(Wskit.ChecksEnabled());
        Console.WriteLine(Wskit.DependencyChecksEnabled());
        Console.WriteLine(Wskit.Halve(8));
        try
        {
            Wskit.Halve(3);
            Console.WriteLine("no exception");
        }
        catch (WskitException ex)
        {
            Console.WriteLine(ex.Status + " " + ex.Message.Contains("odd input"));
        }

        """;

    private readonly DockerWorkspaceEndToEndFixture _docker;

    public DockerWorkspaceEndToEndTests(DockerWorkspaceEndToEndFixture docker)
    {
        _docker = docker;
    }

    [Fact]
    public async Task a_workspace_member_keeps_the_workspaces_patch_cargo_config_and_profiles_in_docker_mode()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        DotnetCli.SkipIfMissing();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("ws/Cargo.toml", "[workspace]\nmembers = [\"wskit\"]\nresolver = \"2\"\n\n[patch.crates-io]\nwskit_patched_dep = { path = \"patched\" }\n\n[profile.release]\ndebug-assertions = true\npanic = \"abort\"\ncodegen-units = 1\n\n[profile.release.package.wskit_patched_dep]\ndebug-assertions = false\n");
        sandbox.WriteFile("ws/.cargo/config.toml", "[env]\nWSKIT_GREETING = \"hello from the workspace config\"\n");
        sandbox.WriteFile("ws/patched/Cargo.toml", "[package]\nname = \"wskit_patched_dep\"\nversion = \"1.0.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n");
        sandbox.WriteFile("ws/patched/src/lib.rs", "pub fn answer() -> i32 {\n    42\n}\n\npub fn checks_enabled() -> bool {\n    cfg!(debug_assertions)\n}\n");
        sandbox.WriteFile("ws/wskit/Cargo.toml", "[package]\nname = \"wskit\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[dependencies]\nwskit_patched_dep = \"1.0\"\n");
        sandbox.WriteFile("ws/wskit/src/lib.rs", MemberLib.Replace("\r\n", "\n") + "\n");
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "ws", "wskit", "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.LinuxX64, CrateTarget.WinX64 },
            OutputDirectory = Path.Combine(sandbox.Directory, "out", "native"),
            Offline = true,
            Wrapper = new WrapperOptions { AssemblyName = "E2E.Workspace", RootNamespace = "E2E.Workspace" },
        };
        using var builder = EndToEndKit.NewBuilder(DockerWorkspaceEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        var wrapperBuild = await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build());

        //Assert
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.ToolchainDescription.Should().StartWith("Docker image ");
        foreach (var target in result.Targets)
        {
            target.CargoResult.CommandLine.Should().Contain("--config", "the workspace's .cargo/config.toml is passed to the facade build in the container");
        }

        var windows = NativeArtifactReader.Read(Path.Combine(request.OutputDirectory, "runtimes", "win-x64", "native", "wskit.dll"));
        windows.Format.Should().Be(NativeArtifactFormat.Pe);
        windows.ExportedSymbols.Should().Contain(new[] { "wskit_answer", "wskit_greeting", "wskit_checks_enabled", "wskit_halve" });
        wrapperBuild.Should().Contain("0 Warning(s)");
        if (WebGpu.WebGpuTestPackage.HostTarget() == CrateTarget.LinuxX64)
        {
            var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(sandbox.Directory, "app"), result.Wrapper.ProjectPath, Program.Replace("\r\n", "\n"));
            // The panic hook's stderr line ("thread ... panicked at ... odd input") is part of the
            // output (see the facade's status -4 conventions), so the lines are checked in order.
            var lines = EndToEndKit.Lines(output);
            lines[^1].Should().Be("-4 True", "the release profile's panic = \"abort\" was replaced, so the panic is caught and becomes status -4");
            lines.Should().ContainInOrder(
                "42",
                "hello from the workspace config",
                "True",
                "False",
                "4",
                "-4 True");
        }

        EndToEndKit.Log(nameof(a_workspace_member_keeps_the_workspaces_patch_cargo_config_and_profiles_in_docker_mode), stopwatch);
    }
}
