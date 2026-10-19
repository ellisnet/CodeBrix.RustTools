using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The public API of one Rust crate as written by codebrix-api-extractor (schema version 1,
/// Facade/Extraction/Tool/SCHEMA.md): crate metadata, modules, functions and methods,
/// structs, enums, type aliases, constants, traits, trait impls, re-exports and diagnostics.
/// Every list is non-null after <see cref="Parse"/>.
/// </summary>
internal sealed class CrateApiModel
{
    /// <summary>The schema version this model reads and writes.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The JSON settings of the document: camelCase names, snake_case enum values,
    /// absent optional values left out, indented.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>The document's schema version.</summary>
    public int SchemaVersion { get; set; } = SupportedSchemaVersion;

    /// <summary>The tool that wrote the document.</summary>
    public RustToolInfo Tool { get; set; } = new RustToolInfo();

    /// <summary>Crate metadata.</summary>
    public RustCrateInfo Crate { get; set; } = new RustCrateInfo();

    /// <summary>Every module, the root first.</summary>
    public List<RustModule> Modules { get; set; } = new List<RustModule>();

    /// <summary>Free functions and inherent-impl methods.</summary>
    public List<RustFunction> Functions { get; set; } = new List<RustFunction>();

    /// <summary>Structs and unions.</summary>
    public List<RustStruct> Structs { get; set; } = new List<RustStruct>();

    /// <summary>Enums.</summary>
    public List<RustEnum> Enums { get; set; } = new List<RustEnum>();

    /// <summary>Type aliases.</summary>
    public List<RustTypeAlias> TypeAliases { get; set; } = new List<RustTypeAlias>();

    /// <summary>Consts, statics and associated consts.</summary>
    public List<RustConstant> Constants { get; set; } = new List<RustConstant>();

    /// <summary>Traits.</summary>
    public List<RustTrait> Traits { get; set; } = new List<RustTrait>();

    /// <summary>Every <c>impl Trait for Type</c>.</summary>
    public List<RustImplInfo> TraitImpls { get; set; } = new List<RustImplInfo>();

    /// <summary>Every name of every <c>pub use</c>.</summary>
    public List<RustReexport> Reexports { get; set; } = new List<RustReexport>();

    /// <summary>What the extractor could not read or skipped.</summary>
    public List<RustDiagnostic> Diagnostics { get; set; } = new List<RustDiagnostic>();

    /// <summary>The file the model was loaded from; <c>null</c> after <see cref="Parse"/>.</summary>
    [JsonIgnore]
    public string SourcePath { get; private set; }

    /// <summary>Functions without an owner.</summary>
    [JsonIgnore]
    public IEnumerable<RustFunction> FreeFunctions => Functions.Where(f => f.Owner == null);

    /// <summary>Methods (functions with an owner).</summary>
    [JsonIgnore]
    public IEnumerable<RustFunction> Methods => Functions.Where(f => f.Owner != null);

    /// <summary>Every item of every kind (functions, structs, enums, aliases, constants,
    /// traits; trait methods are not included).</summary>
    [JsonIgnore]
    public IEnumerable<RustItem> AllItems
        => Functions.Cast<RustItem>().Concat(Structs).Concat(Enums).Concat(TypeAliases).Concat(Constants).Concat(Traits);

    /// <summary>The error diagnostics (files that could not be read or parsed).</summary>
    [JsonIgnore]
    public IEnumerable<RustDiagnostic> Errors => Diagnostics.Where(d => d.Severity == RustDiagnosticSeverity.Error);

    /// <summary>Reads a document from a file.</summary>
    /// <exception cref="InvalidDataException">The file is not a schema-version-1 document.</exception>
    public static CrateApiModel Load(string path)
    {
        var model = Parse(File.ReadAllText(path, Encoding.UTF8));
        model.SourcePath = Path.GetFullPath(path);
        return model;
    }

    /// <summary>Reads a document from JSON text.</summary>
    /// <exception cref="InvalidDataException">The text is not JSON of a schema-version-1
    /// document.</exception>
    public static CrateApiModel Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The API document is empty.");
        }

        CrateApiModel model;
        try
        {
            model = JsonSerializer.Deserialize<CrateApiModel>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The API document is not valid: " + ex.Message, ex);
        }

        if (model == null)
        {
            throw new InvalidDataException("The API document is empty.");
        }

        if (model.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException($"The API document has schema version {model.SchemaVersion}; this CrateBuilder reads version {SupportedSchemaVersion}.");
        }

        model.Normalize();
        return model;
    }

    /// <summary>Writes the document as JSON (the same shape the tool writes).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions).Replace("\r\n", "\n") + "\n";

    /// <summary>Finds an item by its <see cref="RustItem.Id"/>.</summary>
    public RustItem FindById(string id) => AllItems.FirstOrDefault(i => i.Id == id);

    /// <summary>The items whose canonical path or one of whose public paths is
    /// <paramref name="path"/>.</summary>
    public IEnumerable<RustItem> FindByPath(string path) => AllItems.Where(i => i.CanonicalPath == path || i.PublicPaths.Contains(path));

    /// <summary>The struct with the given name or path (canonical or public), or <c>null</c>.</summary>
    public RustStruct FindStruct(string nameOrPath) => Structs.FirstOrDefault(s => Matches(s, nameOrPath));

    /// <summary>The enum with the given name or path, or <c>null</c>.</summary>
    public RustEnum FindEnum(string nameOrPath) => Enums.FirstOrDefault(e => Matches(e, nameOrPath));

    /// <summary>The free function with the given name or path, or <c>null</c>.</summary>
    public RustFunction FindFunction(string nameOrPath) => FreeFunctions.FirstOrDefault(f => Matches(f, nameOrPath));

    /// <summary>The methods whose owner resolved to <paramref name="typeCanonicalPath"/>.</summary>
    public IReadOnlyList<RustFunction> MethodsOf(string typeCanonicalPath)
        => Methods.Where(f => f.Owner.ResolvedPath == typeCanonicalPath).ToList();

    /// <summary>The methods of a struct, enum or alias.</summary>
    public IReadOnlyList<RustFunction> MethodsOf(RustItem type) => MethodsOf(type?.CanonicalPath);

    /// <summary>The method <paramref name="name"/> of the type, or <c>null</c>.</summary>
    public RustFunction FindMethod(RustItem type, string name) => MethodsOf(type).FirstOrDefault(f => f.Name == name);

    /// <summary>The associated consts of a type.</summary>
    public IReadOnlyList<RustConstant> AssociatedConstsOf(RustItem type)
        => Constants.Where(c => c.Owner != null && c.Owner.ResolvedPath == type?.CanonicalPath).ToList();

    /// <summary>The trait impls that target the local type <paramref name="typeCanonicalPath"/>.</summary>
    public IReadOnlyList<RustImplInfo> TraitImplsOf(string typeCanonicalPath) => TraitImpls.Where(t => t.SelfPath == typeCanonicalPath).ToList();

    /// <summary>The local struct, enum or alias a type names (by its last segment when the
    /// path is unambiguous among the recorded items), or <c>null</c>.</summary>
    public RustItem ResolveLocalType(RustType type)
    {
        if (type == null || type.Kind != RustTypeKind.Path || type.Qself != null)
        {
            return null;
        }

        var name = type.PathName;
        var candidates = Structs.Cast<RustItem>().Concat(Enums).Concat(TypeAliases).Where(i => i.Name == name).ToList();
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var written = type.PathWithoutArguments;
        return candidates.FirstOrDefault(c => c.CanonicalPath.EndsWith("::" + written, StringComparison.Ordinal)
            || c.PublicPaths.Any(p => p.EndsWith("::" + written, StringComparison.Ordinal)));
    }

    private static bool Matches(RustItem item, string nameOrPath)
        => item.Name == nameOrPath || item.CanonicalPath == nameOrPath || item.PublicPaths.Contains(nameOrPath);

    private void Normalize()
    {
        Tool ??= new RustToolInfo();
        Crate ??= new RustCrateInfo();
        Crate.CrateTypes ??= new List<string>();
        Crate.DeclaredFeatures ??= new List<string>();
        Crate.RequestedFeatures ??= new List<string>();
        Crate.Docs ??= string.Empty;
        Modules ??= new List<RustModule>();
        Functions ??= new List<RustFunction>();
        Structs ??= new List<RustStruct>();
        Enums ??= new List<RustEnum>();
        TypeAliases ??= new List<RustTypeAlias>();
        Constants ??= new List<RustConstant>();
        Traits ??= new List<RustTrait>();
        TraitImpls ??= new List<RustImplInfo>();
        Reexports ??= new List<RustReexport>();
        Diagnostics ??= new List<RustDiagnostic>();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
