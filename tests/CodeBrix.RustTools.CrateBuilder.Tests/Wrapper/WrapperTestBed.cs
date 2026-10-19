using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Wrapper;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

/// <summary>
/// A sandbox for one wrapper-writer run without cargo: a fake staged crate (Cargo.toml with a
/// licence, two licence files, a workspace-root licence), FAKE native files (empty files with
/// the right names) per compiled target, a wrap report, the request and a context.
/// </summary>
internal sealed class WrapperTestBed : IDisposable
{
    private readonly SampleSandbox _sandbox = SampleSandbox.CreateEmpty();

    public WrapperTestBed(MappingPlan plan, string assemblyName = WrapperPlanFixturesNames.AssemblyName, params CrateTarget[] compiled)
    {
        Plan = plan;
        Root = _sandbox.Directory;
        var crate = _sandbox.CreateDirectory("stage/src/crates/" + plan.CrateName);
        _sandbox.WriteFile("stage/src/Cargo.toml", "[workspace]\nmembers = [\"crates/*\"]\nresolver = \"3\"\n\n[workspace.package]\nversion = \"0.15.2\"\nlicense = \"MIT OR Apache-2.0\"\n");
        _sandbox.WriteFile("stage/src/LICENSE-MIT", "workspace MIT licence text\n");
        _sandbox.WriteFile("stage/src/crates/" + plan.CrateName + "/Cargo.toml", "[package]\nname = \"" + plan.CrateName + "\"\nversion.workspace = true\nedition = \"2024\"\nlicense.workspace = true\n");
        _sandbox.WriteFile("stage/src/crates/" + plan.CrateName + "/LICENSE-MIT", "crate MIT licence text\n");
        _sandbox.WriteFile("stage/src/crates/" + plan.CrateName + "/LICENSE-APACHE", "crate Apache licence text\n");
        Workspace = new StagedWorkspace
        {
            Directory = Path.Combine(Root, "stage"),
            ManifestPath = Path.Combine(crate, "Cargo.toml"),
            UpstreamManifestPath = Path.Combine(crate, "Cargo.toml"),
            WorkspaceManifestPath = Path.Combine(Root, "stage", "src", "Cargo.toml"),
            CrateName = plan.LibName,
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.GitKind, Url = "https://github.com/ellisnet/CodeBrix.RustTools", Commit = "0123456789abcdef0123456789abcdef01234567", ManifestPath = "crates/" + plan.CrateName + "/Cargo.toml", CrateName = plan.CrateName, CrateVersion = "0.15.2" },
        };

        OutputDirectory = Path.Combine(Root, "out", "native");
        var targets = compiled.Length == 0 ? new[] { CrateTarget.LinuxX64, CrateTarget.WinX64 } : compiled;
        foreach (var target in targets)
        {
            var rid = CrateTargets.Rid(target);
            var file = Path.Combine(OutputDirectory, "runtimes", rid, "native", CrateTargets.NativeFileName(target, plan.LibName));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, Array.Empty<byte>());
            CompiledTargets.Add(new CrateTargetResult { Target = target, Rid = rid, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { file } });
        }

        var report = _sandbox.WriteFile("run/facade/WRAP-REPORT.txt", "WRAP REPORT (fixture)\n");
        _sandbox.WriteFile("run/facade/WRAP-REPORT.txt.json", "{ \"fixture\": true }\n");
        Facade = new FacadeGenerationResult { Generated = !plan.UpstreamIsCdylib, Plan = plan, WrapReportPath = report };
        Request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Workspace.ManifestPath),
            OutputDirectory = OutputDirectory,
            Targets = targets.Concat(new[] { CrateTarget.OsxArm64 }).ToList(),
            Wrapper = new WrapperOptions { AssemblyName = assemblyName, RootNamespace = plan.ManagedNamespace },
        };
        Context = new CrateBuilderContext(new CrateBuilderOptions(), Path.Combine(Root, "run"), null, null, null);
    }

    public MappingPlan Plan { get; }

    public string Root { get; }

    public StagedWorkspace Workspace { get; }

    public string OutputDirectory { get; }

    public List<CrateTargetResult> CompiledTargets { get; } = new List<CrateTargetResult>();

    public FacadeGenerationResult Facade { get; }

    public CrateBuildRequest Request { get; }

    public CrateBuilderContext Context { get; }

    /// <summary>The default wrapper folder (sibling of the native output folder).</summary>
    public string DefaultWrapperDirectory => Path.Combine(Root, "out", Request.Wrapper.AssemblyName);

    public Task<WrapperResult> WriteAsync()
        => new WrapperProjectWriter().WriteAsync(Workspace, CompiledTargets, Facade, Request, Context, TestContext.Current.CancellationToken);

    public string Read(WrapperResult result, string relative) => File.ReadAllText(Path.Combine(result.ProjectDirectory, relative));

    public static Dictionary<string, byte[]> Snapshot(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    public void Dispose() => _sandbox.Dispose();
}

/// <summary>Names shared by the wrapper tests.</summary>
internal static class WrapperPlanFixturesNames
{
    public const string AssemblyName = "Fixture.DataCrate";
}
