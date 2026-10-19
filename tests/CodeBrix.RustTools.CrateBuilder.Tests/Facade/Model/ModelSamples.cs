using System.IO;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;

/// <summary>
/// The checked-in extractor documents in Facade/Model/samples/ (written by
/// codebrix-api-extractor for rust-samples/test_project_15 and _17 and for a scratch workspace
/// with edge cases; paths are relative to the folder the tool was run in).
/// </summary>
internal static class ModelSamples
{
    public const string DataCrate = "datacrate-api.json";

    public const string HardCrate = "hardcrate-api.json";

    public const string EdgeCases = "edgecases-api.json";

    public static readonly string[] All = { DataCrate, HardCrate, EdgeCases };

    public static string PathOf(string name)
        => System.IO.Path.Combine(RepoPaths.RepoRoot, "tests", "CodeBrix.RustTools.CrateBuilder.Tests", "Facade", "Model", "samples", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static CrateApiModel Load(string name) => CrateApiModel.Load(PathOf(name));
}
