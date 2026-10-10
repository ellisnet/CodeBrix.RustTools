using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Internal;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// A typed model of one Cargo.toml file: the package, its dependencies by kind and platform,
/// features, the workspace table, explicitly declared targets and profiles. Values are kept
/// as written; workspace inheritance is resolved by <see cref="CargoProject.EffectivePackage"/>.
/// </summary>
public sealed class CargoManifest
{
    /// <summary>The file name cargo looks for, <c>Cargo.toml</c>.</summary>
    public const string FileName = "Cargo.toml";

    /// <summary>The full path of the file, or <c>null</c> when parsed from text.</summary>
    public string FilePath { get; init; }

    /// <summary>The folder containing the file, or <c>null</c> when parsed from text.</summary>
    public string Directory => FilePath == null ? null : System.IO.Path.GetDirectoryName(FilePath);

    /// <summary>The <c>[package]</c> table, or <c>null</c> for a virtual workspace manifest.</summary>
    public CargoPackage Package { get; init; }

    /// <summary>The <c>[workspace]</c> table, or <c>null</c> when this manifest is not a
    /// workspace root.</summary>
    public CargoWorkspace Workspace { get; init; }

    /// <summary>Every dependency of every kind, including platform-specific ones
    /// (<see cref="CargoDependency.Target"/> set), in document order.</summary>
    public IReadOnlyList<CargoDependency> Dependencies { get; init; } = Array.Empty<CargoDependency>();

    /// <summary>The <c>[features]</c> table in document order.</summary>
    public IReadOnlyList<CargoFeature> Features { get; init; } = Array.Empty<CargoFeature>();

    /// <summary>The <c>[lib]</c> table, or <c>null</c> when not declared.</summary>
    public CargoTargetDeclaration Lib { get; init; }

    /// <summary>The <c>[[bin]]</c> tables.</summary>
    public IReadOnlyList<CargoTargetDeclaration> Bins { get; init; } = Array.Empty<CargoTargetDeclaration>();

    /// <summary>The <c>[[example]]</c> tables.</summary>
    public IReadOnlyList<CargoTargetDeclaration> Examples { get; init; } = Array.Empty<CargoTargetDeclaration>();

    /// <summary>The <c>[[test]]</c> tables.</summary>
    public IReadOnlyList<CargoTargetDeclaration> Tests { get; init; } = Array.Empty<CargoTargetDeclaration>();

    /// <summary>The <c>[[bench]]</c> tables.</summary>
    public IReadOnlyList<CargoTargetDeclaration> Benches { get; init; } = Array.Empty<CargoTargetDeclaration>();

    /// <summary>The <c>[profile.*]</c> tables.</summary>
    public IReadOnlyList<CargoProfileDeclaration> Profiles { get; init; } = Array.Empty<CargoProfileDeclaration>();

    /// <summary>True when the manifest has a <c>[workspace]</c> table.</summary>
    public bool IsWorkspaceRoot => Workspace != null;

    /// <summary>True for a virtual manifest: a <c>[workspace]</c> without a <c>[package]</c>.</summary>
    public bool IsVirtual => Package == null && Workspace != null;

    /// <summary>
    /// Returns the dependencies of one kind (all platforms).
    /// </summary>
    /// <param name="kind">Normal, dev or build.</param>
    public IReadOnlyList<CargoDependency> GetDependencies(CargoDependencyKind kind) => Dependencies.Where(d => d.Kind == kind).ToList();

    /// <summary>
    /// Returns the profile with the given name, or <c>null</c>.
    /// </summary>
    /// <param name="name">The profile name, for example <c>release</c>.</param>
    public CargoProfileDeclaration GetProfile(string name) => Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Returns the feature with the given name, or <c>null</c>.
    /// </summary>
    /// <param name="name">The feature name.</param>
    public CargoFeature GetFeature(string name) => Features.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Reads and parses a Cargo.toml file.
    /// </summary>
    /// <param name="path">The path of the Cargo.toml file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="RustToolsException">The file is not valid TOML.</exception>
    public static CargoManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Cargo.toml file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    /// <summary>
    /// Parses the text of a Cargo.toml file.
    /// </summary>
    /// <param name="text">The TOML text.</param>
    /// <param name="filePath">The file's path, recorded in <see cref="FilePath"/> and used in error
    /// messages; may be <c>null</c>.</param>
    /// <exception cref="RustToolsException">The text is not valid TOML.</exception>
    public static CargoManifest Parse(string text, string filePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = TomlHelpers.Parse(text, filePath ?? FileName);

        var packageTable = TomlHelpers.GetTable(document, "package") ?? TomlHelpers.GetTable(document, "project");
        var workspaceTable = TomlHelpers.GetTable(document, "workspace");

        return new CargoManifest
        {
            FilePath = filePath == null ? null : System.IO.Path.GetFullPath(filePath),
            Package = packageTable == null ? null : ParsePackage(packageTable),
            Workspace = workspaceTable == null ? null : ParseWorkspace(workspaceTable),
            Dependencies = ParseAllDependencies(document),
            Features = ParseFeatures(TomlHelpers.GetTable(document, "features")),
            Lib = TomlHelpers.GetTable(document, "lib") is { } lib ? ParseTarget(lib, CargoTargetKind.Lib) : null,
            Bins = ParseTargets(document, "bin", CargoTargetKind.Bin),
            Examples = ParseTargets(document, "example", CargoTargetKind.Example),
            Tests = ParseTargets(document, "test", CargoTargetKind.Test),
            Benches = ParseTargets(document, "bench", CargoTargetKind.Bench),
            Profiles = ParseProfiles(TomlHelpers.GetTable(document, "profile")),
        };
    }

    internal static CargoPackage ParsePackage(TomlTable table)
    {
        var inherited = new List<string>();
        foreach (var pair in table)
        {
            if (pair.Value is TomlTable inner && TomlHelpers.GetBoolean(inner, "workspace") == true)
            {
                inherited.Add(pair.Key);
            }
        }

        var publish = table.TryGetValue("publish", out var publishValue)
            ? publishValue switch
            {
                bool flag => flag,
                TomlArray registries => registries.Count > 0,
                _ => (bool?)null,
            }
            : null;

        var build = table.TryGetValue("build", out var buildValue) ? buildValue : null;

        return new CargoPackage
        {
            Name = TomlHelpers.GetString(table, "name"),
            Version = TomlHelpers.GetString(table, "version"),
            Edition = TomlHelpers.GetString(table, "edition"),
            Description = TomlHelpers.GetString(table, "description"),
            License = TomlHelpers.GetString(table, "license"),
            LicenseFile = TomlHelpers.GetString(table, "license-file"),
            Repository = TomlHelpers.GetString(table, "repository"),
            Homepage = TomlHelpers.GetString(table, "homepage"),
            Documentation = TomlHelpers.GetString(table, "documentation"),
            Readme = TomlHelpers.GetString(table, "readme"),
            RustVersion = TomlHelpers.GetString(table, "rust-version"),
            Authors = TomlHelpers.GetStringList(table, "authors"),
            Keywords = TomlHelpers.GetStringList(table, "keywords"),
            Categories = TomlHelpers.GetStringList(table, "categories"),
            Publish = publish,
            BuildScript = build as string,
            BuildScriptDisabled = build is bool buildFlag && !buildFlag,
            Links = TomlHelpers.GetString(table, "links"),
            DefaultRun = TomlHelpers.GetString(table, "default-run"),
            AutoBins = TomlHelpers.GetBoolean(table, "autobins"),
            AutoExamples = TomlHelpers.GetBoolean(table, "autoexamples"),
            AutoTests = TomlHelpers.GetBoolean(table, "autotests"),
            AutoBenches = TomlHelpers.GetBoolean(table, "autobenches"),
            InheritedFields = inherited,
        };
    }

    private static CargoWorkspace ParseWorkspace(TomlTable table)
    {
        var resolver = table.TryGetValue("resolver", out var resolverValue) ? Convert.ToString(resolverValue, CultureInfo.InvariantCulture) : null;
        var packageTable = TomlHelpers.GetTable(table, "package");
        return new CargoWorkspace
        {
            Members = TomlHelpers.GetStringList(table, "members"),
            DefaultMembers = TomlHelpers.GetStringList(table, "default-members"),
            Exclude = TomlHelpers.GetStringList(table, "exclude"),
            Resolver = resolver,
            Package = packageTable == null ? null : ParsePackage(packageTable),
            Dependencies = ParseDependencyTable(TomlHelpers.GetTable(table, "dependencies"), CargoDependencyKind.Normal, null),
        };
    }

    private static IReadOnlyList<CargoDependency> ParseAllDependencies(TomlTable document)
    {
        var result = new List<CargoDependency>();
        AddDependencyTables(result, document, null);

        if (TomlHelpers.GetTable(document, "target") is { } targets)
        {
            foreach (var pair in targets)
            {
                if (pair.Value is TomlTable platform)
                {
                    AddDependencyTables(result, platform, pair.Key);
                }
            }
        }

        return result;
    }

    private static void AddDependencyTables(List<CargoDependency> result, TomlTable owner, string target)
    {
        result.AddRange(ParseDependencyTable(TomlHelpers.GetTable(owner, "dependencies"), CargoDependencyKind.Normal, target));
        result.AddRange(ParseDependencyTable(TomlHelpers.GetTable(owner, "dev-dependencies") ?? TomlHelpers.GetTable(owner, "dev_dependencies"), CargoDependencyKind.Dev, target));
        result.AddRange(ParseDependencyTable(TomlHelpers.GetTable(owner, "build-dependencies") ?? TomlHelpers.GetTable(owner, "build_dependencies"), CargoDependencyKind.Build, target));
    }

    private static IReadOnlyList<CargoDependency> ParseDependencyTable(TomlTable table, CargoDependencyKind kind, string target)
    {
        var result = new List<CargoDependency>();
        if (table == null)
        {
            return result;
        }

        foreach (var pair in table)
        {
            if (pair.Value is string version)
            {
                result.Add(new CargoDependency
                {
                    Name = pair.Key,
                    PackageName = pair.Key,
                    VersionRequirement = version,
                    Kind = kind,
                    Target = target,
                });
                continue;
            }

            if (pair.Value is not TomlTable detail)
            {
                continue;
            }

            var defaultFeatures = TomlHelpers.GetBoolean(detail, "default-features") ?? TomlHelpers.GetBoolean(detail, "default_features") ?? true;
            result.Add(new CargoDependency
            {
                Name = pair.Key,
                PackageName = TomlHelpers.GetString(detail, "package") ?? pair.Key,
                VersionRequirement = TomlHelpers.GetString(detail, "version"),
                Path = TomlHelpers.GetString(detail, "path"),
                Git = TomlHelpers.GetString(detail, "git"),
                Branch = TomlHelpers.GetString(detail, "branch"),
                Tag = TomlHelpers.GetString(detail, "tag"),
                Rev = TomlHelpers.GetString(detail, "rev"),
                Registry = TomlHelpers.GetString(detail, "registry"),
                Features = TomlHelpers.GetStringList(detail, "features"),
                Optional = TomlHelpers.GetBoolean(detail, "optional") ?? false,
                DefaultFeatures = defaultFeatures,
                IsWorkspaceInherited = TomlHelpers.GetBoolean(detail, "workspace") ?? false,
                Kind = kind,
                Target = target,
            });
        }

        return result;
    }

    private static IReadOnlyList<CargoFeature> ParseFeatures(TomlTable table)
    {
        var result = new List<CargoFeature>();
        if (table == null)
        {
            return result;
        }

        foreach (var pair in table)
        {
            result.Add(new CargoFeature { Name = pair.Key, Enables = TomlHelpers.GetStringList(table, pair.Key) });
        }

        return result;
    }

    private static IReadOnlyList<CargoTargetDeclaration> ParseTargets(TomlTable document, string key, CargoTargetKind kind)
    {
        var result = new List<CargoTargetDeclaration>();
        if (TomlHelpers.GetTableArray(document, key) is { } array)
        {
            foreach (var table in array)
            {
                result.Add(ParseTarget(table, kind));
            }
        }

        return result;
    }

    private static CargoTargetDeclaration ParseTarget(TomlTable table, CargoTargetKind kind)
    {
        var isProcMacro = TomlHelpers.GetBoolean(table, "proc-macro") ?? TomlHelpers.GetBoolean(table, "proc_macro") ?? false;
        var crateTypes = TomlHelpers.GetStringList(table, "crate-type");
        if (crateTypes.Count == 0)
        {
            crateTypes = TomlHelpers.GetStringList(table, "crate_type");
        }

        return new CargoTargetDeclaration
        {
            Name = TomlHelpers.GetString(table, "name"),
            Kind = kind == CargoTargetKind.Lib && isProcMacro ? CargoTargetKind.ProcMacro : kind,
            Path = TomlHelpers.GetString(table, "path")?.Replace('\\', '/'),
            IsDeclared = true,
            RequiredFeatures = TomlHelpers.GetStringList(table, "required-features"),
            CrateTypes = crateTypes,
            Harness = TomlHelpers.GetBoolean(table, "harness") ?? true,
            Test = TomlHelpers.GetBoolean(table, "test"),
            Bench = TomlHelpers.GetBoolean(table, "bench"),
            IsProcMacro = isProcMacro,
        };
    }

    private static IReadOnlyList<CargoProfileDeclaration> ParseProfiles(TomlTable table)
    {
        var result = new List<CargoProfileDeclaration>();
        if (table == null)
        {
            return result;
        }

        foreach (var pair in table)
        {
            if (pair.Value is not TomlTable profile)
            {
                continue;
            }

            result.Add(new CargoProfileDeclaration
            {
                Name = pair.Key,
                OptLevel = ValueText(profile, "opt-level"),
                Inherits = TomlHelpers.GetString(profile, "inherits"),
                Lto = ValueText(profile, "lto"),
                CodegenUnits = TomlHelpers.GetInteger(profile, "codegen-units"),
                Debug = ValueText(profile, "debug"),
                PackageOverrides = TomlHelpers.GetKeys(profile, "package"),
            });
        }

        return result;
    }

    private static string ValueText(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value != null
            ? value switch
            {
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            }
            : null;
}
