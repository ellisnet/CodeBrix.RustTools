using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering;

/// <summary>
/// A staged-workspace stand-in for the pure rendering tests: a sandbox holding a copy of a sample
/// under &lt;sandbox&gt;/stage/src (the stager's layout), without running the stager.
/// </summary>
internal static class RenderingTestWorkspace
{
    public static StagedWorkspace FromSample(SampleSandbox sandbox, int sample, string crateName, string version = "0.1.0")
    {
        var stage = Path.Combine(sandbox.Directory, "stage");
        var source = Path.Combine(stage, "src");
        Copy(RepoPaths.Sample(sample), source);
        var manifest = Path.Combine(source, "Cargo.toml");
        return new StagedWorkspace
        {
            Directory = stage,
            ManifestPath = manifest,
            UpstreamManifestPath = manifest,
            WorkspaceManifestPath = manifest,
            CrateName = crateName,
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind, CrateName = crateName, CrateVersion = version },
        };
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            var name = Path.GetFileName(directory);
            if (name != "target" && !name.StartsWith('.'))
            {
                Copy(directory, Path.Combine(to, name));
            }
        }
    }
}
