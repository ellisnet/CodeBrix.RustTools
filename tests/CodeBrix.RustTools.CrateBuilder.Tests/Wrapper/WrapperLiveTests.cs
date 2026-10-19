using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

/// <summary>
/// Live tests: the generated projects are built, packed and run with the dotnet CLI (skip
/// without it); the pass-through test also builds rust-samples/test_project_13 (a sandbox copy)
/// with cargo (skip without it). Every folder lives in a sandbox under the test output.
/// </summary>
public class WrapperLiveTests
{
    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromMinutes(5);

    private static async Task<string> DotnetAsync(string directory, params string[] arguments)
    {
        var (exitCode, output) = await DotnetCli.RunAsync(directory, DotnetTimeout, arguments);
        exitCode.Should().Be(0, "dotnet " + string.Join(" ", arguments) + " failed in " + directory + ":\n" + output);
        return output;
    }

    private static string[] Build(params string[] extra) => new[] { "build", "-warnaserror" }.Concat(DotnetCli.BuildFlags()).Concat(extra).ToArray();

    [Fact]
    public async Task the_wrapper_for_every_shape_builds_with_warnings_as_errors()
    {
        //Arrange
        DotnetCli.SkipIfMissing();
        WebGpuTestPackage.LocateOrSkip();
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate(linkWebGpu: true));
        bed.Request.LinkToCodeBrixWebGpu = true;
        bed.Request.Wrapper.GenerateSmokeTests = true;
        bed.Request.Wrapper.Packaging = new WrapperPackaging { PackageId = "Fixture.DataCrate.Package", Authors = "Fixture" };
        var result = await bed.WriteAsync();

        var smoke = Path.Combine(bed.Root, "smoke");
        Directory.CreateDirectory(smoke);
        File.WriteAllText(Path.Combine(smoke, "Smoke.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"4.0.2\" />\n    <ProjectReference Include=\"" + result.ProjectPath + "\" />\n    <Compile Include=\"" + Path.Combine(result.ProjectDirectory, "Tests", "*.cs") + "\" />\n  </ItemGroup>\n</Project>\n");

        //Act
        var output = await DotnetAsync(result.ProjectDirectory, Build("-c", "Release"));
        var smokeOutput = await DotnetAsync(smoke, Build());

        //Assert
        output.Should().Contain("0 Warning(s)");
        smokeOutput.Should().Contain("0 Warning(s)", "the generated smoke-test file compiles in a test project");
        var bin = Path.Combine(result.ProjectDirectory, "bin", "Release", "net10.0");
        File.Exists(Path.Combine(bin, "Fixture.DataCrate.dll")).Should().BeTrue();
        File.Exists(Path.Combine(bin, "Fixture.DataCrate.xml")).Should().BeTrue();
        File.Exists(Path.Combine(bin, "runtimes", "linux-x64", "native", "libdatacrate.so")).Should().BeTrue();
        File.Exists(Path.Combine(bin, "runtimes", "win-x64", "native", "datacrate.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task a_pass_through_wrapper_around_sample_13_builds_packs_and_is_called_from_a_console_app()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var host = WebGpuTestPackage.HostTarget();
        if (host == null)
        {
            Assert.Skip("This host is not one of the runtime identifiers CrateBuilder builds for.");
        }

        var target = host.Value;
        var rid = CrateTargets.Rid(target);
        using var sandbox = SampleSandbox.FromSample(13);
        sandbox.WriteFile("LICENSE-MIT", "MIT licence of the sandbox copy\n");
        var root = Path.GetDirectoryName(sandbox.Directory);
        var workbench = new RustWorkbench();
        var build = await workbench.BuildAsync(workbench.OpenProject(sandbox.Directory), new CargoBuildOptions { Locked = true, Profile = CargoProfile.Release }, null, TestContext.Current.CancellationToken);
        build.Success.Should().BeTrue("cargo failed: " + build.StandardError);
        var fileName = CrateTargets.NativeFileName(target, "test_project_13");
        var artifact = build.Artifacts.Where(a => a.Kinds.Contains("cdylib")).SelectMany(a => a.Filenames).Single(f => Path.GetFileName(f) == fileName);

        var plan = WrapperPlanFixtures.PassThrough();
        var workspace = new StagedWorkspace
        {
            Directory = sandbox.Directory,
            ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml"),
            UpstreamManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml"),
            WorkspaceManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml"),
            CrateName = "test_project_13",
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind, CrateName = "test_project_13", CrateVersion = "0.1.0", CrateTypes = new[] { "cdylib", "rlib" } },
        };
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(workspace.ManifestPath),
            OutputDirectory = Path.Combine(root, "out", "native"),
            Targets = new List<CrateTarget> { target },
            Wrapper = new WrapperOptions
            {
                AssemblyName = "Fixture.Adder",
                RootNamespace = "Fixture.Adder",
                GenerateSmokeTests = true,
                Packaging = new WrapperPackaging { PackageId = "Fixture.Adder.Package", Authors = "Fixture", Tags = { "rust" } },
            },
        };
        var compiled = new List<CrateTargetResult> { new CrateTargetResult { Target = target, Rid = rid, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { artifact } } };
        var context = new CrateBuilderContext(new CrateBuilderOptions(), Path.Combine(root, "run"), null, null, null);
        var facade = new FacadeGenerationResult { Generated = false, Plan = plan };

        //Act
        var result = await new WrapperProjectWriter().WriteAsync(workspace, compiled, facade, request, context, TestContext.Current.CancellationToken);
        var buildOutput = await DotnetAsync(result.ProjectDirectory, Build("-c", "Release"));
        var packages = Path.Combine(root, "packages");
        await DotnetAsync(result.ProjectDirectory, new[] { "pack", "-c", "Release", "--no-build", "-o", packages }.Concat(DotnetCli.BuildFlags()).ToArray());

        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>disable</Nullable>\n    <ImplicitUsings>disable</ImplicitUsings>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"" + result.ProjectPath + "\" />\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(app, "Program.cs"), "using System;\nusing Fixture.Adder;\n\nConsole.WriteLine(\"add(2,3)=\" + TestProject13.CbAdd(2, 3));\n");
        await DotnetAsync(app, Build());
        var run = await DotnetAsync(app, "run", "--no-build");

        var copied = Path.Combine(app, "bin", "Debug", "net10.0", "runtimes", rid, "native", fileName);
        var copiedExists = File.Exists(copied);
        File.Delete(copied);
        var (missingExit, missingOutput) = await DotnetCli.RunAsync(app, DotnetTimeout, "run", "--no-build");

        var smoke = Path.Combine(root, "smoke");
        Directory.CreateDirectory(smoke);
        File.WriteAllText(Path.Combine(smoke, "Smoke.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"4.0.2\" />\n    <ProjectReference Include=\"" + result.ProjectPath + "\" />\n    <Compile Include=\"" + Path.Combine(result.ProjectDirectory, "Tests", "*.cs") + "\" />\n  </ItemGroup>\n</Project>\n");
        await DotnetAsync(smoke, Build());
        var smokeRun = await DotnetAsync(smoke, "run", "--no-build");

        //Assert
        buildOutput.Should().Contain("0 Warning(s)");
        run.Should().Contain("add(2,3)=5");
        copiedExists.Should().BeTrue("the ProjectReference copies runtimes/<rid>/native/ into the app's output");
        missingExit.Should().NotBe(0);
        missingOutput.Should().Contain("DllNotFoundException");
        missingOutput.Should().Contain("The native library 'test_project_13' for this machine (" + rid + ") was not found.");
        missingOutput.Should().Contain(Path.Combine("runtimes", rid, "native", fileName));
        missingOutput.Should().Contain("This wrapper includes native files for: " + rid + ".");
        smokeRun.Should().Contain("Failed: 0");
        smokeRun.Should().Contain("Total: 1");

        var package = Directory.GetFiles(packages, "Fixture.Adder.Package.*.nupkg").Should().ContainSingle().Which;
        using var zip = ZipFile.OpenRead(package);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        entries.Should().Contain("runtimes/" + rid + "/native/" + fileName);
        entries.Should().Contain("README.md");
        entries.Should().Contain("AGENT-README.txt");
        entries.Should().Contain("THIRD-PARTY-NOTICES.txt");
        entries.Should().Contain("WRAP-REPORT.txt");
        entries.Should().Contain("licenses/LICENSE-MIT");
        entries.Should().Contain("lib/net10.0/Fixture.Adder.dll");
        entries.Should().NotContain(e => e.StartsWith("Tests/", StringComparison.Ordinal) || e.Contains("SmokeTests"));
    }
}
