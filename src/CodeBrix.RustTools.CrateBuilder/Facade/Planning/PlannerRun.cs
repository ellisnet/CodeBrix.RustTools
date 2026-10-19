using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// One <see cref="MappingPlanner.Plan"/> call: the rule table of plan D10 applied to every item
/// of the API model, in the order of the WP6 brief (visibility and cfg, WrapPlan selection,
/// enums, structs, functions, naming, pass-through, overrides, notes).
/// </summary>
internal sealed class PlannerRun
{
    private const string OverrideAdvice = "Add a FacadeOverride for it if you need it.";

    private static readonly string[] StdRoots = { "std", "core", "alloc" };

    private readonly CrateApiModel api;
    private readonly WrapPlan wrapPlan;
    private readonly PlannerOptions options;
    private readonly IReadOnlyList<CrateTarget> targets;
    private readonly HashSet<string> features;
    private readonly HashSet<string> knownFeatures;
    private readonly MappingPlan plan = new MappingPlan();

    private readonly Dictionary<string, RustItem> typeIndex = new Dictionary<string, RustItem>(StringComparer.Ordinal);
    private readonly Dictionary<RustItem, string> blockedTypes = new Dictionary<RustItem, string>();
    private readonly HashSet<RustItem> excludedTypes = new HashSet<RustItem>();
    private readonly HashSet<RustStruct> structCandidates = new HashSet<RustStruct>();
    private readonly HashSet<RustStruct> deciding = new HashSet<RustStruct>();
    private readonly Dictionary<RustStruct, MirrorStruct> mirrors = new Dictionary<RustStruct, MirrorStruct>();
    private readonly Dictionary<RustItem, HandleType> handles = new Dictionary<RustItem, HandleType>();
    private readonly Dictionary<RustStruct, string> notMirrorReasons = new Dictionary<RustStruct, string>();
    private readonly Dictionary<RustEnum, EnumMapping> enums = new Dictionary<RustEnum, EnumMapping>();
    private readonly Dictionary<object, RustItem> typeSources = new Dictionary<object, RustItem>();

    private readonly List<SkipEntry> skipped = new List<SkipEntry>();
    private readonly List<(RustConstant Item, ConstantMapping Mapping)> constants = new List<(RustConstant, ConstantMapping)>();
    private readonly Dictionary<HandleType, List<SkipEntry>> fieldSkips = new Dictionary<HandleType, List<SkipEntry>>();
    private readonly List<PlannedFunction> functions = new List<PlannedFunction>();
    private readonly SortedSet<string> warnings = new SortedSet<string>(StringComparer.Ordinal);
    private readonly List<RenameRule> renames = new List<RenameRule>();

    private string prefix;
    private string libClass;

    /// <summary>Creates the run.</summary>
    public PlannerRun(CrateApiModel api, WrapPlan wrapPlan, PlannerOptions options)
    {
        this.api = api;
        this.wrapPlan = wrapPlan ?? new WrapPlan();
        this.options = options;
        targets = options.Targets != null && options.Targets.Count > 0 ? options.Targets.Distinct().ToList() : CrateTargets.All;
        features = new HashSet<string>(api.Crate?.RequestedFeatures ?? new List<string>(), StringComparer.Ordinal);
        if (options.EnabledFeatures != null)
        {
            features.UnionWith(options.EnabledFeatures);
        }

        knownFeatures = options.KnownFeatures != null ? new HashSet<string>(options.KnownFeatures, StringComparer.Ordinal) : null;
    }

    /// <summary>Plans and returns the mapping.</summary>
    public MappingPlan Execute()
    {
        var crate = api.Crate ?? new RustCrateInfo();
        if (crate.RootKind == RustRootKind.Bin)
        {
            throw new FacadeGenerationException($"The crate '{crate.Name}' has no library target (only a program), so there is nothing to wrap.", CrateBuildStage.Planning);
        }

        var libName = !string.IsNullOrWhiteSpace(crate.LibName) ? crate.LibName.Trim() : (crate.Name ?? "crate").Trim().Replace('-', '_');
        prefix = libName + "_";
        libClass = PlannerNames.Pascal(libName);
        plan.CrateName = crate.Name;
        plan.LibName = libName;
        plan.FacadeCrateName = libName + "_facade";
        plan.SymbolPrefix = prefix;
        plan.ManagedNamespace = !string.IsNullOrWhiteSpace(options.ManagedNamespace) ? options.ManagedNamespace.Trim() : libClass;
        plan.ManagedAssemblyName = !string.IsNullOrWhiteSpace(options.ManagedAssemblyName) ? options.ManagedAssemblyName.Trim() : plan.ManagedNamespace;
        plan.LinkToCodeBrixWebGpu = options.LinkToCodeBrixWebGpu;
        plan.WrapPlanNotes = string.IsNullOrWhiteSpace(wrapPlan.Notes) ? null : wrapPlan.Notes;
        foreach (var file in (wrapPlan.AdditionalFacadeFiles ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            plan.AdditionalFacadeFiles.Add(file);
        }

        PrepareRenames();
        AddDiagnosticWarnings();
        BuildTypeIndex();

        var exports = api.Functions.Where(f => f.Owner == null && f.IsExternC && f.IsNoMangle).OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        plan.UpstreamIsCdylib = crate.IsCdylib && exports.Count > 0 && !options.ForceFacade;
        if (plan.UpstreamIsCdylib)
        {
            PlanPassThrough(exports);
        }
        else
        {
            if (crate.IsCdylib && exports.Count > 0)
            {
                warnings.Add($"FacadeMode.Always: a C facade is generated for the crate's Rust API although the crate is a cdylib with {exports.Count} extern \"C\" function(s) of its own; those are not passed through.");
                if (!crate.CrateTypes.Any(t => t is "lib" or "rlib"))
                {
                    warnings.Add("The crate's [lib] crate-type lists only cdylib; the generated facade needs it as a Rust library too - add \"rlib\" to crate-type.");
                }
            }
            else if (crate.IsCdylib)
            {
                warnings.Add("The crate is built as a C library (cdylib) but exports no extern \"C\" functions, so a C facade is generated for its Rust API.");
                if (!crate.CrateTypes.Any(t => t is "lib" or "rlib"))
                {
                    warnings.Add("The crate's [lib] crate-type lists only cdylib; the generated facade needs it as a Rust library too - add \"rlib\" to crate-type.");
                }
            }

            PlanFacade();
        }

        ApplyOverrides();
        if (!plan.UpstreamIsCdylib)
        {
            AddReleaseFunctions();
        }

        AssignNames();
        Assemble();
        return plan;
    }

    // ------------------------------------------------------------------ setup

    private void PrepareRenames()
    {
        foreach (var pair in (wrapPlan.Renames ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var value = pair.Value?.Trim();
            if (!PlannerNames.IsCSharpIdentifier(value))
            {
                warnings.Add($"The rename of '{pair.Key}' to '{pair.Value}' is ignored: the new name must be a plain C# identifier.");
                continue;
            }

            renames.Add(new RenameRule { Key = pair.Key.Trim(), Value = value, Forms = PathGlob.Forms(new[] { pair.Key.Trim() }) });
        }
    }

    private void AddDiagnosticWarnings()
    {
        foreach (var d in api.Diagnostics)
        {
            var where = d.File != null ? $" ({d.File}{(d.Line.HasValue ? ":" + d.Line.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)})" : string.Empty;
            if (d.Severity == RustDiagnosticSeverity.Error)
            {
                warnings.Add($"Part of the crate could not be read{where}: {d.Message}. Items declared there are missing from the wrapper.");
            }
            else if (d.Code == "item_macro_not_expanded")
            {
                warnings.Add($"Items generated by macros are not visible to CrateBuilder and are not wrapped{where}: {d.Message}.");
            }
            else if (d.Severity == RustDiagnosticSeverity.Warning)
            {
                warnings.Add($"While reading the crate{where}: {d.Message}.");
            }
        }
    }

    private void BuildTypeIndex()
    {
        foreach (var item in api.Structs.Cast<RustItem>().Concat(api.Enums).Concat(api.TypeAliases).OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            foreach (var path in new[] { item.CanonicalPath }.Concat(item.PublicPaths).Where(p => !string.IsNullOrEmpty(p)))
            {
                typeIndex.TryAdd(path, item);
            }
        }
    }

    // ------------------------------------------------------------------ gates

    private static IReadOnlyList<string> FormsOf(RustItem item)
        => PathGlob.Forms(new[] { item.CanonicalPath }.Concat(item.PublicPaths));

    private static string ShortPath(RustItem item) => PlannerNames.WithoutCrate(item.PrimaryPublicPath ?? item.CanonicalPath);

    private string CfgReason(RustItem item)
    {
        if (item.Cfg.Count == 0)
        {
            return null;
        }

        if (!targets.All(t => CfgEvaluator.EvaluateAll(item.Cfg, t, features, knownFeatures) == false))
        {
            return null;
        }

        // Ruled out only because a cargo feature is off (it would be built with the feature on)?
        return knownFeatures != null && targets.Any(t => CfgEvaluator.EvaluateAll(item.Cfg, t, features) != false)
            ? $"is only built with a cargo feature this build does not enable ({string.Join(" and ", item.Cfg)}), so it is not wrapped. Add the feature to the request's Features to wrap it."
            : $"is only built for {string.Join(" and ", item.Cfg)}; none of the requested targets builds it, so it is not wrapped.";
    }

    private static string HiddenReason(RustItem item)
        => item.IsDocHidden ? "is hidden from the crate's documentation (#[doc(hidden)]), which usually means it is not meant to be used from outside the crate." : null;

    private string TypeFilterReason(IReadOnlyCollection<string> forms)
    {
        if (wrapPlan.IncludeTypes != null && wrapPlan.IncludeTypes.Any(p => !string.IsNullOrWhiteSpace(p)) && !PathGlob.AnyMatch(wrapPlan.IncludeTypes, forms))
        {
            return "is not selected by your WrapPlan's IncludeTypes, so it and its methods are not wrapped.";
        }

        return PathGlob.AnyMatch(wrapPlan.ExcludeTypes, forms) ? "is excluded by your WrapPlan's ExcludeTypes, so it and its methods are not wrapped." : null;
    }

    private string FunctionFilterReason(IReadOnlyCollection<string> forms)
    {
        if (wrapPlan.IncludeFunctions != null && wrapPlan.IncludeFunctions.Any(p => !string.IsNullOrWhiteSpace(p)) && !PathGlob.AnyMatch(wrapPlan.IncludeFunctions, forms))
        {
            return "is not selected by your WrapPlan's IncludeFunctions.";
        }

        return PathGlob.AnyMatch(wrapPlan.ExcludeFunctions, forms) ? "is excluded by your WrapPlan's ExcludeFunctions." : null;
    }

    private void Skip(RustItem item, string kind, string reason, string hint)
        => skipped.Add(new SkipEntry
        {
            Forms = FormsOf(item),
            Item = new SkippedItem { RustPath = ShortPath(item), Kind = kind, Reason = reason, OverrideHint = hint },
        });

    private void SkipType(RustItem item, string kind, string reason, string blockedPhrase)
    {
        Skip(item, kind, reason, OverrideHints.ForType(prefix + PlannerNames.Snake(item.Name), ShortPath(item), kind));
        blockedTypes[item] = blockedPhrase;
    }

    private void SkipFunction(RustFunction f, string reason)
        => Skip(f, f.Owner != null ? "method" : "function", reason, OverrideHints.ForFunction(HintCName(f), ShortPath(f), f.Signature));

    private string HintCName(RustFunction f)
    {
        if (f.Owner != null)
        {
            var owner = f.Owner.ResolvedPath != null && typeIndex.TryGetValue(f.Owner.ResolvedPath, out var item) ? item.Name : f.Owner.Type?.PathName ?? "type";
            return prefix + PlannerNames.Snake(owner) + "_" + PlannerNames.Snake(f.Name);
        }

        return prefix + string.Join("_", ShortPath(f).Split("::").Select(PlannerNames.Snake));
    }

    // ------------------------------------------------------------------ facade mode

    private void PlanFacade()
    {
        PlanEnums();
        PlanStructs();
        foreach (var f in api.Functions.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            PlanFunction(f);
        }

        AddFieldAccessors();
        var omittedAccessors = OmitFieldAccessors();
        DropUnusedHandles();
        if (omittedAccessors != null)
        {
            var kept = new HashSet<HandleType>(handles.Values);
            plan.FieldAccessorsOmitted = omittedAccessors.Count(f => f.OwnerType is HandleType owner && kept.Contains(owner));
        }

        AddGeneratedHandleFunctions();
        PlanConstants();
    }

    private void PlanEnums()
    {
        foreach (var e in api.Enums.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (!e.IsReachable)
            {
                continue;
            }

            var gate = CfgReason(e) ?? HiddenReason(e);
            if (gate != null)
            {
                SkipType(e, "enum", gate, $"{e.Name}, which is not wrapped");
                continue;
            }

            var filter = TypeFilterReason(FormsOf(e));
            if (filter != null)
            {
                SkipType(e, "enum", filter, $"{e.Name}, which your WrapPlan does not wrap");
                excludedTypes.Add(e);
                continue;
            }

            if (e.HasGenerics)
            {
                SkipType(e, "enum", $"is generic ({e.Generics.Text}): a C enum needs one concrete type. {OverrideAdvice}", $"{e.Name}, a generic enum");
                continue;
            }

            if (e.Generics?.Lifetimes?.Count > 0)
            {
                SkipType(e, "enum", $"borrows data for a lifetime ({e.Generics.Text}): a wrapped value must own everything it holds, because .NET keeps it for as long as it likes. {OverrideAdvice}", $"{e.Name}, an enum that borrows data");
                continue;
            }

            if (!e.IsCLike || e.HasDataVariants)
            {
                // An enum whose variants carry data is an opaque object (like any non-Copy
                // struct): it lives in Rust, its methods are called on it, and a generated
                // Variant() names the variant it holds. Its data is never laid out in C (that
                // would need a tagged union per variant).
                var handle = new HandleType
                {
                    RustPath = ShortPath(e),
                    RustTypeName = e.Name,
                    IsCopy = false,
                    HasDefault = e.Implements("Default"),
                    HasClone = e.Implements("Clone"),
                    Doc = e.Docs ?? string.Empty,
                };
                handles[e] = handle;
                typeSources[handle] = e;
                continue;
            }

            if (e.Variants.Count == 0)
            {
                SkipType(e, "enum", "has no variants, so no value of it can exist.", $"{e.Name}, an enum without variants");
                continue;
            }

            if (e.Variants.Any(v => v.Cfg.Count > 0))
            {
                SkipType(e, "enum", $"has variants that exist only on some targets, so its values differ between platforms. {OverrideAdvice}", $"{e.Name}, an enum whose variants depend on the target");
                continue;
            }

            var mapping = new EnumMapping { RustPath = ShortPath(e), RustTypeName = e.Name, Doc = e.Docs ?? string.Empty };
            long next = 0;
            string problem = null;
            foreach (var v in e.Variants)
            {
                if (v.Discriminant != null && v.DiscriminantValue == null)
                {
                    problem = $"has a variant value that is not a plain number ({v.Name} = {v.Discriminant}). {OverrideAdvice}";
                    break;
                }

                var value = v.DiscriminantValue ?? next;
                if (value < int.MinValue || value > int.MaxValue)
                {
                    problem = $"has a variant value that does not fit in 32 bits ({v.Name} = {value.ToString(CultureInfo.InvariantCulture)}). {OverrideAdvice}";
                    break;
                }

                mapping.Variants.Add(new EnumVariantMapping { RustName = v.Name, Value = value, Doc = v.Docs ?? string.Empty });
                next = value + 1;
            }

            if (problem != null)
            {
                SkipType(e, "enum", problem, $"{e.Name}, an enum with unusual values");
                continue;
            }

            enums[e] = mapping;
            typeSources[mapping] = e;
        }
    }

    private void PlanStructs()
    {
        foreach (var s in api.Structs.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!s.IsReachable)
            {
                continue;
            }

            var kind = s.Shape == RustItemShape.Union ? "union" : "struct";
            var gate = CfgReason(s) ?? HiddenReason(s);
            if (gate != null)
            {
                SkipType(s, kind, gate, $"{s.Name}, which is not wrapped");
                continue;
            }

            var filter = TypeFilterReason(FormsOf(s));
            if (filter != null)
            {
                SkipType(s, kind, filter, $"{s.Name}, which your WrapPlan does not wrap");
                excludedTypes.Add(s);
                continue;
            }

            if (s.Shape == RustItemShape.Union)
            {
                SkipType(s, kind, $"is a union (its fields share memory), which is not wrapped automatically. {OverrideAdvice}", $"{s.Name}, a union");
                continue;
            }

            if (s.HasGenerics)
            {
                SkipType(s, kind, $"is generic ({s.Generics.Text}): a C type needs one concrete type. {OverrideAdvice}", $"{s.Name}, a generic type");
                continue;
            }

            if (s.Generics?.Lifetimes?.Count > 0)
            {
                SkipType(s, kind, $"borrows data for a lifetime ({s.Generics.Text}): a wrapped object must own everything it holds, because .NET keeps it for as long as it likes. {OverrideAdvice}", $"{s.Name}, a type that borrows data");
                continue;
            }

            structCandidates.Add(s);
        }

        foreach (var s in structCandidates.OrderBy(s => s.Id, StringComparer.Ordinal).ToList())
        {
            Decide(s);
        }
    }

    private void Decide(RustStruct s)
    {
        if (mirrors.ContainsKey(s) || handles.ContainsKey(s) || !structCandidates.Contains(s) || !deciding.Add(s))
        {
            return;
        }

        try
        {
            var reason = NotMirrorReason(s, out var fields);
            if (reason == null)
            {
                var mirror = new MirrorStruct
                {
                    RustPath = ShortPath(s),
                    RustTypeName = s.Name,
                    Doc = s.Docs ?? string.Empty,
                    IsTupleStruct = s.Shape == RustItemShape.Tuple,
                    TupleArity = s.Shape == RustItemShape.Tuple ? s.TupleArity : 0,
                };
                var offset = 0;
                var align = 1;
                foreach (var (field, mapping) in fields)
                {
                    var a = TypeMappings.AlignmentOf(mapping);
                    offset = Align(offset, a);
                    align = Math.Max(align, a);
                    mirror.Fields.Add(new MirrorField
                    {
                        Name = field.Name ?? field.Index.ToString(CultureInfo.InvariantCulture),
                        CName = field.Name != null ? PlannerNames.SafeC(field.Name) : "item" + field.Index.ToString(CultureInfo.InvariantCulture),
                        Type = mapping,
                        Doc = field.Docs ?? string.Empty,
                        Offset = offset,
                    });
                    offset += SizeOf(mapping);
                }

                mirror.Alignment = align;
                mirror.ByteSize = Align(offset, align);
                mirrors[s] = mirror;
                typeSources[mirror] = s;
            }
            else
            {
                notMirrorReasons[s] = reason;
                var handle = new HandleType
                {
                    RustPath = ShortPath(s),
                    RustTypeName = s.Name,
                    IsCopy = false,
                    HasDefault = s.Implements("Default"),
                    HasClone = s.Implements("Clone"),
                    Doc = s.Docs ?? string.Empty,
                };
                handles[s] = handle;
                typeSources[handle] = s;
            }
        }
        finally
        {
            deciding.Remove(s);
        }
    }

    private string NotMirrorReason(RustStruct s, out List<(RustField Field, TypeMapping Mapping)> fields)
    {
        fields = new List<(RustField, TypeMapping)>();
        if (!s.IsCopy)
        {
            return "is not Copy (it may own memory or other resources)";
        }

        if (!s.AllFieldsPublic)
        {
            return "has private fields";
        }

        if (s.Fields.Count == 0)
        {
            return "has no fields";
        }

        if (s.HasAttribute("#[non_exhaustive"))
        {
            return "is #[non_exhaustive], so code outside its crate cannot build a value of it from its fields";
        }

        if (s.Repr.Any(r => r.StartsWith("packed", StringComparison.Ordinal) || r.StartsWith("align", StringComparison.Ordinal)))
        {
            return $"uses a special memory layout (#[repr({string.Join(", ", s.Repr)})])";
        }

        if (s.Fields.Any(f => f.Cfg.Count > 0))
        {
            return "has fields that exist only on some targets";
        }

        var scope = new Scope { Module = s.Module, Owner = s };
        foreach (var field in s.Fields)
        {
            var mapping = ValueMapping(Classify(field.Type, scope, 0));
            if (mapping == null)
            {
                return $"has the field '{field.Name ?? field.Index.ToString(CultureInfo.InvariantCulture)}' of type {field.Type?.Text}, which cannot be copied to C as a plain value";
            }

            fields.Add((field, mapping));
        }

        return null;
    }

    private static int Align(int value, int alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    private static int SizeOf(TypeMapping mapping) => mapping.Kind == MappingKind.MirrorStruct ? mapping.Mirror.ByteSize : mapping.ByteSize;

    // ------------------------------------------------------------------ functions

    private void PlanFunction(RustFunction f)
    {
        if (!f.IsReachable)
        {
            return;
        }

        var gate = CfgReason(f) ?? HiddenReason(f);
        if (gate != null)
        {
            SkipFunction(f, gate);
            return;
        }

        RustItem ownerItem = null;
        HandleType ownerHandle = null;
        MirrorStruct ownerMirror = null;
        EnumMapping ownerEnum = null;
        if (f.Owner != null)
        {
            if (f.Owner.ResolvedPath == null || !typeIndex.TryGetValue(f.Owner.ResolvedPath, out ownerItem))
            {
                SkipFunction(f, $"belongs to a type CrateBuilder could not identify ({f.Owner.TypeText}). {OverrideAdvice}");
                return;
            }

            if (excludedTypes.Contains(ownerItem))
            {
                return;
            }

            if (ownerItem is RustTypeAlias)
            {
                SkipFunction(f, $"is defined on the type alias {ownerItem.Name}, which is not wrapped automatically. {OverrideAdvice}");
                return;
            }

            if (ownerItem is RustStruct os)
            {
                Decide(os);
                mirrors.TryGetValue(os, out ownerMirror);
                handles.TryGetValue(os, out ownerHandle);
            }
            else if (ownerItem is RustEnum oe)
            {
                enums.TryGetValue(oe, out ownerEnum);
                handles.TryGetValue(oe, out ownerHandle);
            }

            if (ownerHandle == null && ownerMirror == null && ownerEnum == null)
            {
                var why = blockedTypes.TryGetValue(ownerItem, out var phrase) ? phrase : $"{ownerItem.Name}, which is not wrapped";
                SkipFunction(f, $"belongs to {why}. {OverrideAdvice}");
                return;
            }
        }

        var filter = FunctionFilterReason(FormsOf(f));
        if (filter != null)
        {
            SkipFunction(f, filter);
            return;
        }

        Dictionary<string, Substitution> textGenerics = null;
        if (f.HasGenerics && !TryTextGenerics(f, out textGenerics))
        {
            var generics = !string.IsNullOrEmpty(f.Generics?.Text) ? f.Generics.Text : f.Owner?.Generics?.Text;
            SkipFunction(f, $"is generic ({generics}): a C function needs one concrete type, and CrateBuilder cannot know which types you will use (only type parameters bounded by AsRef<str>, AsRef<Path>, AsRef<OsStr>, Into<String>, Into<PathBuf> or AsRef<[u8]> are filled in automatically, with a string or a byte list). Add a FacadeOverride for each type you need.");
            return;
        }

        if (f.IsAsync)
        {
            SkipFunction(f, $"is async (it returns a future that must be awaited): a C function has to return when it is done, so the future would need an executor (which runtime, which thread, how to cancel), and that is a choice CrateBuilder cannot make for you. {OverrideAdvice} (for example one that blocks on the future with the runtime you use).");
            return;
        }

        if (f.IsUnsafe && !options.IncludeUnsafeFunctions)
        {
            SkipFunction(f, "is an unsafe fn: calling it correctly needs knowledge the generated code does not have. Set IncludeUnsafeFunctions to wrap it anyway, or add a FacadeOverride.");
            return;
        }

        if (f.Abi != null && f.Abi != "C" && f.Abi != "Rust")
        {
            SkipFunction(f, $"uses the \"{f.Abi}\" calling convention, which is not wrapped automatically. {OverrideAdvice}");
            return;
        }

        var scope = new Scope { Module = f.Module, Owner = ownerItem, Substitutions = textGenerics };
        var exported = new ExportedFunction
        {
            RustPath = ShortPath(f),
            Doc = f.Docs ?? string.Empty,
            IsUnsafeUpstream = f.IsUnsafe,
            ReturnsStatus = true,
            OwnerRustPath = ownerItem != null ? ShortPath(ownerItem) : null,
            Kind = FunctionKind.Free,
        };
        var args = new List<string>();
        var used = new List<HandleType>();
        object ownerType = (object)ownerHandle ?? (object)ownerMirror ?? ownerEnum;

        // receiver
        if (f.SelfKind == RustSelfKind.Typed)
        {
            SkipFunction(f, $"takes self as {f.SelfType?.Text}, which is not wrapped automatically. {OverrideAdvice}");
            return;
        }

        if (f.HasReceiver)
        {
            if (ownerHandle != null)
            {
                exported.Owner = ownerHandle;
                exported.Kind = f.SelfKind switch
                {
                    RustSelfKind.Ref => FunctionKind.Method,
                    RustSelfKind.RefMut => FunctionKind.MutMethod,
                    _ => FunctionKind.ConsumingMethod,
                };
                exported.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(ownerHandle, f.Owner.TypeText), Role = ParameterRole.Handle });
                args.Add(MappingPlanner.ReceiverLocal);
                used.Add(ownerHandle);
            }
            else
            {
                if (f.SelfKind == RustSelfKind.RefMut)
                {
                    SkipFunction(f, $"changes the {ownerItem.Name} it is called on in place (&mut self); values are passed to C by copy, so this is not wrapped automatically. An override can return the changed value instead.");
                    return;
                }

                var selfMapping = ownerMirror != null ? TypeMappings.Mirror(ownerMirror, f.Owner.TypeText) : TypeMappings.Enum(ownerEnum, f.Owner.TypeText);
                exported.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.ValueReceiverParameterName, ManagedName = "value", Type = selfMapping, Role = ParameterRole.Value });
                args.Add(f.SelfKind == RustSelfKind.Ref ? "&" + MappingPlanner.ReceiverLocal : MappingPlanner.ReceiverLocal);
            }
        }

        // parameters
        foreach (var p in f.Params)
        {
            var ty = Classify(TextBoundStandIn(p.Type) ?? p.Type, scope, 0);
            var failure = MapParameter(ty, p.Name, exported.Parameters, out var arg);
            if (failure != null)
            {
                SkipFunction(f, failure);
                return;
            }

            args.Add(arg);
            used.AddRange(ty.Handles());
        }

        // result
        var returnType = Classify(f.ReturnType ?? new RustType { Kind = RustTypeKind.Unit, Text = "()" }, scope, 0);
        if (textGenerics != null && TextGenericsUsedIn(f.ReturnType, textGenerics))
        {
            SkipFunction(f, $"is generic ({f.Generics?.Text}) and returns its type parameter, so the caller would choose the result type; a C function needs one concrete type. {OverrideAdvice}");
            return;
        }

        var copied = OwnedReturn(returnType, f.HasReceiver, out var returnAdapter, out var copyNote, out var copyFailure);
        if (copyFailure != null)
        {
            SkipFunction(f, copyFailure);
            return;
        }

        if (copied != null)
        {
            returnType = copied;
            if (copyNote != null)
            {
                exported.Doc = string.IsNullOrWhiteSpace(exported.Doc) ? copyNote : exported.Doc.TrimEnd() + "\n\n" + copyNote;
            }
        }

        var returnFailure = MapReturn(returnType, f.HasReceiver, out var mapping, out var outs);
        if (returnFailure != null)
        {
            SkipFunction(f, returnFailure);
            return;
        }

        foreach (var o in outs)
        {
            exported.Parameters.Add(o);
        }

        exported.Return = mapping;
        used.AddRange(returnType.Handles());

        if (!f.HasReceiver && ownerHandle != null && IsConstructorResult(returnType, ownerHandle))
        {
            exported.Kind = FunctionKind.Constructor;
            exported.Owner = ownerHandle;
        }

        var call = $"{MappingPlanner.UpstreamCrateAlias}::{PlannerNames.WithoutCrate(f.PrimaryPublicPath)}({string.Join(", ", args)})";
        exported.RustCallExpression = returnAdapter != null ? returnAdapter.Replace("{0}", call, StringComparison.Ordinal) : call;
        if (mapping.ErrorFormat != null && mapping.ErrorFormat != "Display")
        {
            warnings.Add(mapping.ErrorFormat == "Debug"
                ? $"{exported.RustPath}: the error type {mapping.ErrorTypeDisplay} has no Display implementation CrateBuilder can see, so the error message uses its Debug text."
                : $"{exported.RustPath}: the error type {mapping.ErrorTypeDisplay} has neither a Display nor a Debug implementation CrateBuilder can see, so the error message is only the type name.");
        }

        DedupeParameterNames(exported);
        functions.Add(new PlannedFunction
        {
            Function = exported,
            Forms = FormsOf(f),
            OwnerType = ownerType,
            Suffix = PlannerNames.Snake(ShortPath(f).Split("::").Last()),
            FreePath = ShortPath(f),
            Uses = used.Distinct().ToList(),
        });
    }

    private static bool IsConstructorResult(PlannedType ty, HandleType owner)
    {
        var core = ty.Kind is PlannedTypeKind.Option or PlannedTypeKind.Result ? ty.Inner : ty;
        return core != null && core.Kind == PlannedTypeKind.Handle && core.Handle == owner;
    }

    private static void DedupeParameterNames(ExportedFunction f)
    {
        var cNames = new HashSet<string>(StringComparer.Ordinal);
        var managedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in f.Parameters)
        {
            p.CName = PlannerNames.Unique(p.CName, cNames, "_");
            p.ManagedName = PlannerNames.Unique(p.ManagedName, managedNames, string.Empty);
        }
    }

    private static string Takes(PlannedType ty, string parameter)
    {
        var bad = ty.FirstUnsupported() ?? ty;
        var description = bad.Description ?? $"{ty.Text}";
        var advice = bad.Advice != null ? " " + bad.Advice : string.Empty;
        return $"takes {description} in its parameter '{parameter}', which is not wrapped automatically.{advice} {OverrideAdvice}";
    }

    private static string Returns(PlannedType ty, string description = null, string advice = null)
    {
        var bad = ty.FirstUnsupported() ?? ty;
        description ??= bad.Description ?? ty.Text;
        advice ??= bad.Advice;
        return $"returns {description}, which is not wrapped automatically.{(advice != null ? " " + advice : string.Empty)} {OverrideAdvice}";
    }

    private string MapParameter(PlannedType ty, string rustName, IList<ParameterMapping> parameters, out string arg)
    {
        arg = rustName;
        var c = PlannerNames.SafeC(rustName);
        var m = PlannerNames.Camel(rustName);
        switch (ty.Kind)
        {
            case PlannedTypeKind.Primitive:
            case PlannedTypeKind.Bool:
            case PlannedTypeKind.Enum:
            case PlannedTypeKind.Mirror:
            case PlannedTypeKind.Array:
            {
                var mapping = ValueMapping(ty);
                if (mapping == null)
                {
                    return Takes(ArrayProblem(ty), rustName);
                }

                parameters.Add(Param(rustName, c, m, mapping, ParameterRole.Value));
                return null;
            }

            case PlannedTypeKind.String:
                AddString(parameters, rustName, c, m, TypeMappings.StringInput(MappingKind.String, ty.Text));
                arg = OwnedFromText(ty, rustName);
                return null;

            case PlannedTypeKind.Vec:
                if (ty.Inner?.Kind != PlannedTypeKind.Primitive)
                {
                    var element = ArgumentElement(ty.Inner);
                    if (element == null)
                    {
                        return Takes(ListProblem(ty), rustName);
                    }

                    AddSlice(parameters, rustName, c, m, TypeMappings.ValueSlice(element, ty.Text));
                    return null;
                }

                AddSlice(parameters, rustName, c, m, TypeMappings.VecInput(ty.Inner.Primitive, ty.Text));
                arg = rustName + ".to_vec()";
                return null;

            case PlannedTypeKind.Handle:
                return Takes(PlannedType.Unsupported(ty.Text, $"ownership of a {ty.Handle.RustTypeName} object ({ty.Text})", $"Only borrowed objects (&{ty.Handle.RustTypeName}) are passed automatically."), rustName);

            case PlannedTypeKind.Reference:
                return MapReferenceParameter(ty, rustName, c, m, parameters, ref arg);

            case PlannedTypeKind.Option:
                return MapOptionParameter(ty, rustName, c, m, parameters, ref arg);

            case PlannedTypeKind.Tuple:
                return Takes(PlannedType.Unsupported(ty.Text, $"a tuple ({ty.Text})", "Tuples are wrapped automatically only as results."), rustName);

            case PlannedTypeKind.Result:
                return Takes(PlannedType.Unsupported(ty.Text, $"a Result value ({ty.Text})"), rustName);

            case PlannedTypeKind.Pointer:
                return Takes(PlannedType.Unsupported(ty.Text, $"a raw pointer ({ty.Text})"), rustName);

            case PlannedTypeKind.Unit:
                return Takes(PlannedType.Unsupported(ty.Text, "an empty value (())"), rustName);

            default:
                return Takes(ty, rustName);
        }
    }

    private string MapReferenceParameter(PlannedType ty, string rustName, string c, string m, IList<ParameterMapping> parameters, ref string arg)
    {
        var inner = ty.Inner;
        switch (inner.Kind)
        {
            case PlannedTypeKind.Str when !ty.Mutable:
                AddString(parameters, rustName, c, m, TypeMappings.StringInput(MappingKind.Str, ty.Text));
                arg = inner.StdText switch
                {
                    "Path" => "::std::path::Path::new(" + rustName + ")",
                    "OsStr" => "::std::ffi::OsStr::new(" + rustName + ")",
                    _ => rustName,
                };
                return null;
            case PlannedTypeKind.Slice when !ty.Mutable && inner.Inner?.Kind != PlannedTypeKind.Primitive && ArgumentElement(inner.Inner) != null:
                AddSlice(parameters, rustName, c, m, TypeMappings.ValueSlice(ArgumentElement(inner.Inner), ty.Text));
                arg = "&" + rustName;
                return null;
            case PlannedTypeKind.Slice:
                if (inner.Inner?.Kind != PlannedTypeKind.Primitive)
                {
                    return Takes(inner.Inner?.Kind == PlannedTypeKind.Unsupported ? inner.Inner : PlannedType.Unsupported(ty.Text, $"a slice of {inner.Inner?.Text} values ({ty.Text})", "Only slices of numbers, strings, objects whose type is Clone and plain values are wrapped automatically (&mut slices only of numbers)."), rustName);
                }

                AddSlice(parameters, rustName, c, m, TypeMappings.Slice(inner.Inner.Primitive, ty.Mutable, ty.Text));
                return null;
            case PlannedTypeKind.Handle:
            {
                // &mut Object borrows the object mutably: the facade must use handle_mut.
                var handle = TypeMappings.Handle(inner.Handle, inner.Text);
                handle.IsMutable = ty.Mutable;
                parameters.Add(Param(rustName, c, m, handle, ParameterRole.Handle));
                return null;
            }
            case PlannedTypeKind.String when !ty.Mutable:
                AddString(parameters, rustName, c, m, TypeMappings.StringInput(MappingKind.String, ty.Text));
                arg = "&" + OwnedFromText(inner, rustName);
                return null;
            case PlannedTypeKind.Vec when !ty.Mutable && inner.Inner?.Kind == PlannedTypeKind.Primitive:
                AddSlice(parameters, rustName, c, m, TypeMappings.VecInput(inner.Inner.Primitive, ty.Text));
                arg = "&" + rustName + ".to_vec()";
                return null;
            case PlannedTypeKind.Vec when !ty.Mutable && ArgumentElement(inner.Inner) != null:
                AddSlice(parameters, rustName, c, m, TypeMappings.ValueSlice(ArgumentElement(inner.Inner), ty.Text));
                arg = "&" + rustName;
                return null;
            case PlannedTypeKind.Primitive:
            case PlannedTypeKind.Bool:
            case PlannedTypeKind.Enum:
            case PlannedTypeKind.Mirror:
            case PlannedTypeKind.Array:
            {
                if (ty.Mutable)
                {
                    return Takes(PlannedType.Unsupported(ty.Text, $"a {inner.Text} to change in place ({ty.Text})", "Values are passed to C by copy; an override can return the changed value instead."), rustName);
                }

                var mapping = ValueMapping(inner);
                if (mapping == null)
                {
                    return Takes(ArrayProblem(inner), rustName);
                }

                parameters.Add(Param(rustName, c, m, mapping, ParameterRole.Value));
                arg = "&" + rustName;
                return null;
            }

            default:
                if (inner.FirstUnsupported() != null)
                {
                    return Takes(inner, rustName);
                }

                return Takes(PlannedType.Unsupported(ty.Text, $"a reference to {inner.Text} ({ty.Text})"), rustName);
        }
    }

    private string MapOptionParameter(PlannedType ty, string rustName, string c, string m, IList<ParameterMapping> parameters, ref string arg)
    {
        var inner = ty.Inner;
        var hasC = PlannerNames.SafeC("has_" + PlannerNames.Bare(rustName));
        var hasM = PlannerNames.EscapeCSharp("has" + PlannerNames.Pascal(rustName));
        var value = inner.Kind == PlannedTypeKind.Reference && !inner.Mutable ? inner.Inner : inner;
        if (value.Kind == PlannedTypeKind.Handle && inner.Kind == PlannedTypeKind.Reference)
        {
            parameters.Add(Param(rustName, hasC, hasM, TypeMappings.Bool(), ParameterRole.InPresent));
            parameters.Add(Param(rustName, c, m, TypeMappings.Handle(value.Handle, value.Text), ParameterRole.Handle));
            return null;
        }

        // Option<&str> / Option<String>: a present flag + the text (a .NET null is None)
        if ((inner.Kind == PlannedTypeKind.Reference && !inner.Mutable && value.Kind == PlannedTypeKind.Str && value.StdText == null)
            || (inner.Kind == PlannedTypeKind.String && inner.StdText == null))
        {
            parameters.Add(Param(rustName, hasC, hasM, TypeMappings.Bool(), ParameterRole.InPresent));
            AddString(parameters, rustName, c, m, TypeMappings.StringInput(value.Kind == PlannedTypeKind.Str ? MappingKind.Str : MappingKind.String, ty.Text));
            if (inner.Kind == PlannedTypeKind.String)
            {
                arg = rustName + ".map(::std::borrow::ToOwned::to_owned)";
            }

            return null;
        }

        var mapping = value.Kind is PlannedTypeKind.Primitive or PlannedTypeKind.Bool or PlannedTypeKind.Enum or PlannedTypeKind.Mirror or PlannedTypeKind.Array ? ValueMapping(value) : null;
        if (mapping == null)
        {
            var bad = ty.FirstUnsupported();
            return Takes(bad ?? PlannedType.Unsupported(ty.Text, $"an optional {inner.Text} ({ty.Text})", "Only optional numbers, plain values and borrowed objects are wrapped automatically."), rustName);
        }

        parameters.Add(Param(rustName, hasC, hasM, TypeMappings.Bool(), ParameterRole.InPresent));
        parameters.Add(Param(rustName, c, m, mapping, ParameterRole.Value));
        if (value != inner)
        {
            arg = rustName + ".as_ref()";
        }

        return null;
    }

    private static PlannedType ArrayProblem(PlannedType ty)
    {
        var element = ty;
        while (element.Kind == PlannedTypeKind.Array)
        {
            element = element.Inner;
        }

        return element.Kind == PlannedTypeKind.Unsupported ? element : PlannedType.Unsupported(ty.Text, $"an array of {element.Text} values ({ty.Text})", "Only arrays of numbers are wrapped automatically.");
    }

    /// <summary>The element mapping of a list that can be RETURNED: a plain value
    /// (<see cref="ValueElement"/>), an object (every element becomes its own object) or a
    /// String; <c>null</c> otherwise.</summary>
    private TypeMapping ResultElement(PlannedType element)
    {
        if (element == null)
        {
            return null;
        }

        return element.Kind switch
        {
            PlannedTypeKind.Handle => TypeMappings.Handle(element.Handle, element.Text),
            PlannedTypeKind.String when element.StdText == null => TypeMappings.StringOutput(prefix, element.Text),
            _ => ValueElement(element),
        };
    }

    /// <summary>The element mapping of a list or slice ARGUMENT: a plain value
    /// (<see cref="ValueElement"/>), an object whose type is Clone (cloned into the list), or a
    /// string (&amp;str or String); <c>null</c> otherwise.</summary>
    private TypeMapping ArgumentElement(PlannedType element)
    {
        if (element == null)
        {
            return null;
        }

        if (element.Kind == PlannedTypeKind.Handle)
        {
            return element.Handle.HasClone ? TypeMappings.Handle(element.Handle, element.Text) : null;
        }

        if (element.Kind == PlannedTypeKind.String && element.StdText == null)
        {
            return TypeMappings.StringListElement(prefix, MappingKind.String, element.Text);
        }

        if (element.Kind == PlannedTypeKind.Reference && !element.Mutable && element.Inner?.Kind == PlannedTypeKind.Str && element.Inner.StdText == null)
        {
            return TypeMappings.StringListElement(prefix, MappingKind.Str, element.Text);
        }

        return ValueElement(element);
    }

    /// <summary>Whether a list or slice element can be copied (Clone).</summary>
    private bool CloneableElement(PlannedType element)
        => element != null && (element.Kind == PlannedTypeKind.Primitive
            || (element.Kind == PlannedTypeKind.Handle && element.Handle.HasClone)
            || (element.Kind == PlannedTypeKind.String && element.StdText == null)
            || ValueElement(element) != null);

    private static PlannedType ListProblem(PlannedType ty)
        => PlannedType.Unsupported(ty.Text, $"a list of {ty.Inner?.Text} values ({ty.Text})", "Only lists of numbers, strings, objects (Clone ones when passed in) and plain values (Copy structs that cross C by value, C-like enums, bool, arrays of numbers) are wrapped automatically.");

    /// <summary>The element mapping of a list or slice of plain values that are not numbers
    /// (a mirror struct, a C-like enum that is Clone, bool, an array of numbers), or
    /// <c>null</c>.</summary>
    private TypeMapping ValueElement(PlannedType element)
    {
        if (element == null || element.Kind is not (PlannedTypeKind.Mirror or PlannedTypeKind.Enum or PlannedTypeKind.Bool or PlannedTypeKind.Array))
        {
            return null;
        }

        if (element.Kind == PlannedTypeKind.Enum && typeSources.TryGetValue(element.Enum, out var source) && source is RustEnum e && !e.Implements("Clone") && !e.IsCopy)
        {
            return null;
        }

        return ValueMapping(element);
    }

    private static ParameterMapping Param(string rustName, string c, string m, TypeMapping type, ParameterRole role)
        => new ParameterMapping { RustName = rustName, CName = c, ManagedName = m, Type = type, Role = role };

    private static void AddString(IList<ParameterMapping> parameters, string rustName, string c, string m, TypeMapping mapping)
    {
        parameters.Add(Param(rustName, PlannerNames.SafeC(PlannerNames.Bare(rustName) + "_ptr"), m, mapping, ParameterRole.StringData));
        parameters.Add(Param(rustName, PlannerNames.SafeC(PlannerNames.Bare(rustName) + "_len"), m.TrimStart('@') + "Length", TypeMappings.Usize(), ParameterRole.StringLength));
    }

    private static void AddSlice(IList<ParameterMapping> parameters, string rustName, string c, string m, TypeMapping mapping)
    {
        parameters.Add(Param(rustName, PlannerNames.SafeC(PlannerNames.Bare(rustName) + "_ptr"), m, mapping, ParameterRole.SliceData));
        parameters.Add(Param(rustName, PlannerNames.SafeC(PlannerNames.Bare(rustName) + "_len"), m.TrimStart('@') + "Length", TypeMappings.Usize(), ParameterRole.SliceLength));
    }

    private string MapReturn(PlannedType ty, bool hasReceiver, out ReturnMapping mapping, out List<ParameterMapping> outs)
    {
        mapping = new ReturnMapping();
        outs = new List<ParameterMapping>();
        if (ty.Kind == PlannedTypeKind.Unit)
        {
            mapping.Shape = ReturnShape.Void;
            mapping.Type = TypeMappings.Unit();
            return null;
        }

        var borrowed = FirstReference(ty);
        if (borrowed != null)
        {
            var from = hasReceiver ? "the object it is called on" : "its arguments";
            return borrowed == ty
                ? Returns(ty, $"a reference ({borrowed.Text}) that borrows from {from}", "An override can return an owned copy (for example a String) instead.")
                : Returns(ty, $"{ty.Text}, which holds a reference ({borrowed.Text}) that borrows from {from}", "Only a reference on its own (or in an Option) is copied automatically; an override can build an owned value instead.");
        }

        if (ty.Kind == PlannedTypeKind.Vec && ty.Inner?.Kind != PlannedTypeKind.Primitive && ResultElement(ty.Inner) == null)
        {
            return Returns(ListProblem(ty));
        }

        if (ty.FirstUnsupported() != null)
        {
            return Returns(ty);
        }

        switch (ty.Kind)
        {
            case PlannedTypeKind.Option:
            {
                var inner = ty.Inner;
                if (inner.Kind == PlannedTypeKind.Handle)
                {
                    mapping.Shape = ReturnShape.OptionalHandle;
                    mapping.Type = TypeMappings.Handle(inner.Handle, inner.Text);
                    outs.Add(Out("out", "result", mapping.Type, ParameterRole.OutValue));
                    return null;
                }

                var o = OutFor(inner, "out", "result");
                if (o == null)
                {
                    return Returns(ty, $"an optional {inner.Text} ({ty.Text})", "Only optional values, strings, lists of numbers and objects are wrapped automatically.");
                }

                mapping.Shape = ReturnShape.OptionalValue;
                mapping.Type = o.Type;
                outs.Add(Out("out_present", "resultPresent", TypeMappings.Bool(), ParameterRole.OutPresent));
                outs.Add(o);
                return null;
            }

            case PlannedTypeKind.Result:
            {
                mapping.ErrorTypeDisplay = ty.ErrorType?.Text ?? "Error";
                mapping.ErrorFormat = ErrorFormat(ty);
                var ok = ty.Inner;
                if (ok.Kind == PlannedTypeKind.Unit)
                {
                    mapping.Shape = ReturnShape.ResultVoid;
                    mapping.Type = TypeMappings.Unit();
                    return null;
                }

                if (ok.Kind == PlannedTypeKind.Tuple)
                {
                    var tupleFailure = MapTuple(ok, mapping, outs);
                    if (tupleFailure != null)
                    {
                        return Returns(ty, tupleFailure);
                    }

                    mapping.Shape = ReturnShape.ResultTuple;
                    return null;
                }

                var o = OutFor(ok, "out", "result");
                if (o == null)
                {
                    return Returns(ty, $"a Result of {ok.Text} ({ty.Text})", "Only results of values, strings, lists of numbers, objects and tuples of those are wrapped automatically.");
                }

                mapping.Type = o.Type;
                mapping.Shape = ok.Kind switch
                {
                    PlannedTypeKind.Handle => ReturnShape.ResultHandle,
                    PlannedTypeKind.String => ReturnShape.ResultString,
                    PlannedTypeKind.Vec => ReturnShape.ResultBuffer,
                    _ => ReturnShape.ResultValue,
                };
                outs.Add(o);
                return null;
            }

            case PlannedTypeKind.Tuple:
            {
                var tupleFailure = MapTuple(ty, mapping, outs);
                if (tupleFailure != null)
                {
                    return Returns(ty, tupleFailure);
                }

                mapping.Shape = ReturnShape.Tuple;
                return null;
            }

            default:
            {
                var o = OutFor(ty, "out", "result");
                if (o == null)
                {
                    if (ty.Kind == PlannedTypeKind.Vec)
                    {
                        return Returns(ListProblem(ty));
                    }

                    if (ty.Kind == PlannedTypeKind.Array)
                    {
                        return Returns(ArrayProblem(ty));
                    }

                    return Returns(ty, $"{ty.Text}");
                }

                mapping.Type = o.Type;
                mapping.Shape = ty.Kind switch
                {
                    PlannedTypeKind.Handle => ReturnShape.Handle,
                    PlannedTypeKind.String => ReturnShape.OwnedString,
                    PlannedTypeKind.Vec => ReturnShape.OwnedBuffer,
                    _ => ReturnShape.Value,
                };
                outs.Add(o);
                return null;
            }
        }
    }

    // ------------------------------------------------------------------ text-like generics, paths, borrowed results

    /// <summary>The bounds a text or byte argument satisfies: a parameter bounded by one of them
    /// (as <c>impl Bound</c> or as a type parameter) is passed a <c>&amp;str</c> (or
    /// <c>&amp;[u8]</c>), which implements every one of them.</summary>
    private static readonly HashSet<string> TextBounds = new HashSet<string>(StringComparer.Ordinal)
    {
        "AsRef<str>", "AsRef<Path>", "AsRef<std::path::Path>", "AsRef<OsStr>", "AsRef<std::ffi::OsStr>",
        "Into<String>", "Into<PathBuf>", "Into<std::path::PathBuf>", "Into<OsString>", "Into<std::ffi::OsString>",
        "ToString", "std::string::ToString", "AsRef<[u8]>",
    };

    private static RustType StrReference() => new RustType { Kind = RustTypeKind.Reference, Text = "&str", Mutable = false, Inner = new RustType { Kind = RustTypeKind.Primitive, Name = "str", Text = "str" } };

    private static RustType BytesReference() => new RustType { Kind = RustTypeKind.Reference, Text = "&[u8]", Mutable = false, Inner = new RustType { Kind = RustTypeKind.Slice, Text = "[u8]", Elem = new RustType { Kind = RustTypeKind.Primitive, Name = "u8", Text = "u8" } } };

    /// <summary>The stand-in argument type for a set of trait bounds, or <c>null</c> when the
    /// bounds are not exactly one text/byte bound (lifetimes and <c>?Sized</c> aside).</summary>
    private static RustType TextStandIn(IEnumerable<string> bounds)
    {
        var real = (bounds ?? Enumerable.Empty<string>()).Select(b => b.Replace(" ", string.Empty)).Where(b => !b.StartsWith('\'') && b != "?Sized").ToList();
        if (real.Count != 1 || !TextBounds.Contains(real[0]))
        {
            return null;
        }

        return real[0] == "AsRef<[u8]>" ? BytesReference() : StrReference();
    }

    /// <summary><c>impl AsRef&lt;Path&gt;</c> (and the other text bounds) in a parameter: the
    /// argument is passed as a <c>&amp;str</c>.</summary>
    private static RustType TextBoundStandIn(RustType type)
        => type != null && type.Kind == RustTypeKind.ImplTrait ? TextStandIn(type.Bounds) : null;

    /// <summary>A generic function whose every type parameter has exactly one text/byte bound
    /// and no where clause: each parameter is filled in with <c>&amp;str</c> (or
    /// <c>&amp;[u8]</c>), which rustc infers from the argument.</summary>
    private static bool TryTextGenerics(RustFunction f, out Dictionary<string, Substitution> map)
    {
        map = null;
        var g = f.Generics;
        if (g == null || g.TypeParams.Count == 0 || g.ConstParams.Count > 0 || !string.IsNullOrWhiteSpace(g.WhereClause)
            || (f.Owner?.Generics != null && f.Owner.Generics.HasTypeOrConstParams))
        {
            return false;
        }

        var result = new Dictionary<string, Substitution>(StringComparer.Ordinal);
        foreach (var tp in g.TypeParams)
        {
            var standIn = TextStandIn(tp.Bounds);
            if (standIn == null || tp.Default != null || !f.Params.Any(p => p.Type != null && p.Type.Kind == RustTypeKind.Path && p.Type.IsSingleName(tp.Name)))
            {
                return false;
            }

            result[tp.Name] = new Substitution { Type = standIn, Scope = new Scope { Module = "crate" } };
        }

        map = result;
        return true;
    }

    private static bool TextGenericsUsedIn(RustType type, Dictionary<string, Substitution> map)
    {
        if (type == null || map == null)
        {
            return false;
        }

        var text = " " + (type.Text ?? string.Empty) + " ";
        return map.Keys.Any(k => System.Text.RegularExpressions.Regex.IsMatch(text, "[^A-Za-z0-9_]" + System.Text.RegularExpressions.Regex.Escape(k) + "[^A-Za-z0-9_]"));
    }

    /// <summary>The owned value an argument of a String-like type is built from the &amp;str
    /// local <paramref name="local"/>.</summary>
    private static string OwnedFromText(PlannedType ty, string local) => ty.StdText switch
    {
        "PathBuf" => "::std::path::PathBuf::from(" + local + ")",
        "OsString" => "::std::ffi::OsString::from(" + local + ")",
        _ => local + ".to_owned()",
    };

    private const string NotUtf8Path = "\"the path is not valid UTF-8, so it cannot be returned as a .NET string\"";

    /// <summary>Turns a result that cannot cross C as it is into one that can, by a copy that is
    /// always safe: a shared reference becomes an owned copy of the value it points to (the
    /// borrow ends inside the facade call); a path or OS string becomes UTF-8 text (an error,
    /// never a lossy conversion, when it is not UTF-8). Returns <c>null</c> when the result needs
    /// no change; <paramref name="adapter"/> wraps the call ("{0}").</summary>
    private PlannedType OwnedReturn(PlannedType ty, bool hasReceiver, out string adapter, out string note, out string failure)
    {
        adapter = null;
        note = null;
        failure = null;
        if (ty.Kind == PlannedTypeKind.String && ty.StdText != null)
        {
            adapter = "({0}).to_str().map(::std::string::String::from).ok_or(" + NotUtf8Path + ")";
            return TextResult(ty);
        }

        var from = hasReceiver ? "the object it is called on" : "its arguments (or static data)";
        if (ty.Kind == PlannedTypeKind.Reference)
        {
            if (ty.Mutable)
            {
                failure = Returns(ty, $"a mutable reference ({ty.Text}) that borrows from {from}", "It exists so that the caller can change the value in place; a copy would silently drop such a change, so it is never copied automatically.");
                return null;
            }

            var owned = CopyOf(ty.Inner, out var single, out _, out var why);
            if (owned == null)
            {
                failure = why ?? Returns(ty, $"a reference ({ty.Text}) that borrows from {from}", "An override can return an owned copy instead.");
                return null;
            }

            adapter = single;
            note = "The result is a copy: the Rust function returns a reference to the value.";
            return owned;
        }

        if (ty.Kind == PlannedTypeKind.Option && ty.Inner?.Kind == PlannedTypeKind.Reference)
        {
            if (ty.Inner.Mutable)
            {
                failure = Returns(ty, $"an optional mutable reference ({ty.Text}) that borrows from {from}", "It exists so that the caller can change the value in place; a copy would silently drop such a change, so it is never copied automatically.");
                return null;
            }

            var owned = CopyOf(ty.Inner.Inner, out _, out var optional, out var why);
            if (owned == null || optional == null)
            {
                failure = why ?? Returns(ty, $"an optional reference ({ty.Text}) that borrows from {from}", "An override can return an owned copy instead.");
                return null;
            }

            adapter = optional;
            note = "The result is a copy: the Rust function returns a reference to the value.";
            return Wrap(PlannedTypeKind.Option, ty.Text, owned, false);
        }

        return null;
    }

    private static PlannedType TextResult(PlannedType ty)
    {
        var result = Wrap(PlannedTypeKind.Result, ty.Text, new PlannedType { Kind = PlannedTypeKind.String, Text = "String" }, false);
        result.ErrorType = StrReference();
        return result;
    }

    /// <summary>The owned copy of a value of type <paramref name="x"/> behind a shared reference
    /// ("{0}" = the reference), for a single reference and inside an <c>Option</c>.</summary>
    private PlannedType CopyOf(PlannedType x, out string single, out string optional, out string failure)
    {
        const string Clone = "::std::clone::Clone::clone({0})";
        const string Cloned = "({0}).cloned()";
        single = null;
        optional = null;
        failure = null;
        switch (x.Kind)
        {
            case PlannedTypeKind.Primitive:
            case PlannedTypeKind.Bool:
            case PlannedTypeKind.Array:
            case PlannedTypeKind.Mirror:
                single = Clone;
                optional = Cloned;
                return x;
            case PlannedTypeKind.Enum:
                if (typeSources.TryGetValue(x.Enum, out var source) && source is RustEnum e && !e.Implements("Clone") && !e.IsCopy)
                {
                    failure = Returns(x, $"a reference to {x.Text}, which is neither Copy nor Clone, so no copy of it can be made", null);
                    return null;
                }

                single = Clone;
                optional = Cloned;
                return x;
            case PlannedTypeKind.Str when x.StdText == null:
                single = "::std::string::String::from({0})";
                optional = "({0}).map(::std::string::String::from)";
                return new PlannedType { Kind = PlannedTypeKind.String, Text = "String" };
            case PlannedTypeKind.Str:
            case PlannedTypeKind.String when x.StdText != null:
                single = "({0}).to_str().map(::std::string::String::from).ok_or(" + NotUtf8Path + ")";
                return TextResult(x);
            case PlannedTypeKind.Slice when CloneableElement(x.Inner):
                single = "({0}).to_vec()";
                optional = "({0}).map(|__cb_s| __cb_s.to_vec())";
                return Wrap(PlannedTypeKind.Vec, "Vec<" + x.Inner.Text + ">", x.Inner, false);
            case PlannedTypeKind.Vec when !CloneableElement(x.Inner):
                return null;
            case PlannedTypeKind.Option when x.Inner?.Kind == PlannedTypeKind.Handle:
                if (!x.Inner.Handle.HasClone)
                {
                    return null;
                }

                single = Clone;
                return x;
            case PlannedTypeKind.String:
            case PlannedTypeKind.Vec:
                single = Clone;
                optional = Cloned;
                return x;
            case PlannedTypeKind.Handle:
                if (!x.Handle.HasClone)
                {
                    failure = Returns(x, $"a reference to a {x.Handle.RustTypeName} object it does not hand over", $"{x.Handle.RustTypeName} is not Clone, so no independent copy can be made; a wrapped object must own its value. {OverrideAdvice}");
                    return null;
                }

                single = Clone;
                optional = Cloned;
                return x;
            case PlannedTypeKind.Option when x.Inner != null && x.Inner.Kind is PlannedTypeKind.Primitive or PlannedTypeKind.Bool or PlannedTypeKind.Mirror or PlannedTypeKind.Array or PlannedTypeKind.String or PlannedTypeKind.Vec or PlannedTypeKind.Enum:
                if ((x.Inner.Kind == PlannedTypeKind.String && x.Inner.StdText != null) || (x.Inner.Kind == PlannedTypeKind.Enum && CopyOf(x.Inner, out _, out _, out _) == null))
                {
                    return null;
                }

                single = Clone;
                return x;
            default:
                return null;
        }
    }

    private string MapTuple(PlannedType tuple, ReturnMapping mapping, List<ParameterMapping> outs)
    {
        mapping.Type = null;
        for (var i = 0; i < tuple.Items.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var o = OutFor(tuple.Items[i], "out" + index, "result" + index);
            if (o == null)
            {
                return $"a tuple with an element of type {tuple.Items[i].Text} ({tuple.Text})";
            }

            mapping.TupleItems.Add(o.Type);
            outs.Add(o);
        }

        return null;
    }

    private ParameterMapping OutFor(PlannedType ty, string c, string m)
    {
        switch (ty.Kind)
        {
            case PlannedTypeKind.Primitive:
            case PlannedTypeKind.Bool:
            case PlannedTypeKind.Enum:
            case PlannedTypeKind.Mirror:
            case PlannedTypeKind.Array:
            {
                var mapping = ValueMapping(ty);
                return mapping == null ? null : Out(c, m, mapping, ParameterRole.OutValue);
            }

            case PlannedTypeKind.Handle:
                return Out(c, m, TypeMappings.Handle(ty.Handle, ty.Text), ParameterRole.OutValue);
            case PlannedTypeKind.String when ty.StdText == null:
                return Out(c, m, TypeMappings.StringOutput(prefix, ty.Text), ParameterRole.OutBuffer);
            case PlannedTypeKind.Vec when ty.Inner?.Kind == PlannedTypeKind.Primitive:
                return Out(c, m, TypeMappings.VecOutput(prefix, ty.Inner.Primitive, ty.Text), ParameterRole.OutBuffer);
            case PlannedTypeKind.Vec when ResultElement(ty.Inner) != null:
                return Out(c, m, TypeMappings.ValueVec(prefix, ResultElement(ty.Inner), ty.Text), ParameterRole.OutBuffer);
            default:
                return null;
        }
    }

    private static ParameterMapping Out(string c, string m, TypeMapping type, ParameterRole role)
        => new ParameterMapping { RustName = "return", CName = c, ManagedName = m, Type = type, Role = role };

    private static PlannedType FirstReference(PlannedType ty)
    {
        if (ty.Kind == PlannedTypeKind.Reference)
        {
            return ty;
        }

        if (ty.Inner != null && ty.Kind != PlannedTypeKind.Pointer)
        {
            var nested = FirstReference(ty.Inner);
            if (nested != null)
            {
                return nested;
            }
        }

        return ty.Items.Select(FirstReference).FirstOrDefault(r => r != null);
    }

    /// <summary>Whether a type derives an error type whose derive also writes Display from
    /// <c>#[error("...")]</c> messages (thiserror's <c>Error</c> derive): a derive named
    /// <c>Error</c> together with at least one <c>#[error(...)]</c> attribute on the type or
    /// its variants.</summary>
    internal static bool DerivesErrorWithMessages(IEnumerable<string> derives, IEnumerable<string> attributes)
        => (derives ?? Enumerable.Empty<string>()).Any(d => d == "Error" || d.EndsWith("::Error", StringComparison.Ordinal))
            && (attributes ?? Enumerable.Empty<string>()).Any(a => a.Replace(" ", string.Empty).StartsWith("#[error(", StringComparison.Ordinal));

    private string ErrorFormat(PlannedType result)
    {
        var item = result.ErrorItem;
        while (item is RustTypeAlias alias && alias.Type != null)
        {
            item = ResolveLocal(alias.Type, new Scope { Module = alias.Module });
        }

        switch (item)
        {
            case RustStruct s:
                return s.Implements("Display") || DerivesErrorWithMessages(s.Derives, s.Attributes) ? "Display" : s.Implements("Debug") ? "Debug" : "TypeName";
            case RustEnum e:
                return e.Implements("Display") || DerivesErrorWithMessages(e.Derives, e.Attributes.Concat(e.Variants.SelectMany(v => v.Attributes))) ? "Display" : e.Implements("Debug") ? "Debug" : "TypeName";
        }

        var type = result.ErrorType;
        if (type == null || type.Kind == RustTypeKind.Path || (type.Kind == RustTypeKind.Reference && type.Inner != null && type.Inner.IsPrimitiveNamed("str")))
        {
            return "Display";
        }

        return type.Kind == RustTypeKind.Primitive ? "Display" : "Debug";
    }

    // ------------------------------------------------------------------ handles: usage, generated, release

    private static bool IsAccessor(PlannedFunction pf) => pf.Generated is "getter" or "setter" or "fields" or "variant";

    private void DropUnusedHandles()
    {
        // An object is kept when the crate's own functions use it, or when a consumer can make
        // one (a constructor from its public fields, or Default plus field accessors); the
        // objects its kept accessors hand out are kept with it.
        var used = new HashSet<HandleType>(functions.Where(f => !IsAccessor(f)).SelectMany(f => f.Uses));
        foreach (var h in handles.Values)
        {
            var accessors = functions.Where(f => IsAccessor(f) && f.OwnerType == h).ToList();
            if (accessors.Any(f => f.Generated == "fields") || (h.HasDefault && accessors.Count > 0))
            {
                used.Add(h);
            }
        }

        bool grew;
        do
        {
            grew = false;
            foreach (var f in functions.Where(f => IsAccessor(f) && f.OwnerType is HandleType owner && used.Contains(owner)).ToList())
            {
                foreach (var u in f.Uses)
                {
                    grew |= used.Add(u);
                }
            }
        }
        while (grew);

        functions.RemoveAll(f => IsAccessor(f) && f.OwnerType is HandleType owner && !used.Contains(owner));
        foreach (var pair in fieldSkips.Where(p => used.Contains(p.Key)).OrderBy(p => p.Key.RustPath, StringComparer.Ordinal))
        {
            skipped.AddRange(pair.Value);
        }

        foreach (var pair in handles.OrderBy(p => p.Key.Id, StringComparer.Ordinal).ToList())
        {
            if (used.Contains(pair.Value))
            {
                continue;
            }

            var item = pair.Key;
            var methods = api.MethodsOf(item).Count;
            if (item is RustEnum dataEnum)
            {
                var shapes = string.Join(", ", dataEnum.Variants.Where(v => v.Shape != RustItemShape.Unit).Select(v => v.Name));
                handles.Remove(item);
                typeSources.Remove(pair.Value);
                functions.RemoveAll(pf => pf.OwnerType == pair.Value && pf.Generated == "variant");
                SkipType(item, "enum", $"is an enum whose variants carry data ({shapes}); it is wrapped as an object only when a wrapped function makes, takes or returns it, and none does (its data is never laid out in C: that would need a tagged union per variant). {OverrideAdvice}", $"{item.Name}, an enum whose variants carry data that no wrapped function makes");
                continue;
            }

            var s = (RustStruct)item;
            var why = notMirrorReasons.TryGetValue(s, out var r) ? r : "is not a plain value";
            var fieldsWhy = !s.Fields.Any(f => f.IsPublic)
                ? "it has no public fields"
                : !s.AllFieldsPublic || s.HasAttribute("#[non_exhaustive")
                    ? "it cannot be built from its fields (" + (!s.AllFieldsPublic ? "some are private" : "it is #[non_exhaustive]") + ") and has no Default"
                    : "not all of its fields have types that cross C (see the field entries), so it cannot be built from them";
            if (!options.GenerateFieldAccessors && s.Fields.Any(f => f.IsPublic))
            {
                fieldsWhy = (pair.Value.HasDefault ? fieldsWhy.Replace(" and has no Default", string.Empty, StringComparison.Ordinal) : fieldsWhy)
                    + ", its public fields get no getters or setters because WrapperOptions.GenerateFieldAccessors is false"
                    + (pair.Value.HasDefault ? " (so an object made by its Default could not be read or changed)" : string.Empty);
            }
            else if (pair.Value.HasDefault && s.Fields.Any(f => f.IsPublic))
            {
                // Kept for its accessors when it had any: a Default object dropped here has
                // public fields none of which crosses C.
                fieldsWhy = fieldsWhy.Replace(" and has no Default", string.Empty, StringComparison.Ordinal)
                    + ", and although it has Default, none of its public fields can cross C (see the field entries), so an object made by its Default could not be read or changed";
            }

            var reason = methods > 0
                ? $"has no wrappable members: none of its public functions can be wrapped automatically (see them in this list), {fieldsWhy}, and it cannot be passed to C as a plain value because it {why}. {OverrideAdvice}"
                : $"has no wrappable members: it has no public methods or constructors, {fieldsWhy}, and it cannot be passed to C as a plain value because it {why}. {OverrideAdvice}";
            handles.Remove(s);
            typeSources.Remove(pair.Value);
            foreach (var planned in functions.Where(pf => pf.OwnerType == pair.Value))
            {
                planned.OwnerType = null;
                planned.Function.OwnerRustPath = null;
            }

            SkipType(s, "struct", reason, $"{s.Name}, which has no wrappable members");
        }
    }

    // ------------------------------------------------------------------ public fields of objects

    /// <summary>With <see cref="PlannerOptions.GenerateFieldAccessors"/> off: removes the
    /// planned getters and setters of public fields (and the "no getter" entries of fields
    /// whose getter could not be planned) BEFORE unused objects are dropped, so an object is
    /// no longer kept for its accessors alone, and returns the removed functions; <c>null</c>
    /// when the accessors stay. The constructor from the fields and the data-carrying enums'
    /// variant functions stay either way.</summary>
    private List<PlannedFunction> OmitFieldAccessors()
    {
        if (options.GenerateFieldAccessors)
        {
            return null;
        }

        var removed = functions.Where(f => f.Generated is "getter" or "setter").ToList();
        functions.RemoveAll(f => f.Generated is "getter" or "setter");
        foreach (var list in fieldSkips.Values)
        {
            list.Clear();
        }

        return removed;
    }

    /// <summary>Gives every object (a non-Copy struct) a getter and a setter for each public
    /// field whose type crosses C, and a constructor from its fields when every field is
    /// public and crosses C (not for #[non_exhaustive] structs). A getter returns a copy of
    /// the field (an object field only when its type is Clone).</summary>
    private void AddFieldAccessors()
    {
        foreach (var pair in handles.OrderBy(p => p.Key.Id, StringComparer.Ordinal).ToList())
        {
            if (pair.Key is RustEnum e)
            {
                AddVariantName(e, pair.Value);
                continue;
            }

            var s = (RustStruct)pair.Key;
            var h = pair.Value;
            var methodNames = new HashSet<string>(api.MethodsOf(s).Select(m => PlannerNames.Bare(m.Name)), StringComparer.Ordinal);
            var scope = new Scope { Module = s.Module, Owner = s };
            var skips = new List<SkipEntry>();
            fieldSkips[h] = skips;
            var constructible = s.Fields.Count > 0 && s.AllFieldsPublic && !s.HasAttribute("#[non_exhaustive") && s.Fields.All(f => f.Cfg.Count == 0)
                && s.Fields.All(f => f.Name == null || (PlannerNames.Bare(f.Name) != MappingPlanner.ReceiverLocal && !f.Name.StartsWith("__cb", StringComparison.Ordinal)));
            var ctor = new ExportedFunction
            {
                RustPath = h.RustPath + "::from_fields",
                Doc = $"Builds a {s.Name} from the values of its fields.",
                Kind = FunctionKind.Constructor,
                Owner = h,
                ReturnsStatus = true,
                OwnerRustPath = h.RustPath,
            };
            var ctorArgs = new List<string>();
            var ctorUses = new List<HandleType> { h };
            foreach (var field in s.Fields)
            {
                if (!field.IsPublic || field.Cfg.Count > 0)
                {
                    continue;
                }

                var name = field.Name ?? field.Index.ToString(CultureInfo.InvariantCulture);
                var bare = PlannerNames.Bare(name);
                var label = field.Name != null ? bare : "item" + name;
                var access = MappingPlanner.ReceiverLocal + "." + name;
                var ty = Classify(field.Type, scope, 0);
                var fieldForms = SuffixForms(s, bare);

                // getter
                if (!methodNames.Contains(bare) && !methodNames.Contains("get_" + bare) && FunctionFilterReason(fieldForms) == null)
                {
                    var failure = PlanGetter(s, h, field, label, access, ty, fieldForms);
                    if (failure != null)
                    {
                        skips.Add(new SkipEntry { Forms = fieldForms, Item = new SkippedItem { RustPath = h.RustPath + "::" + label, Kind = "field", Reason = failure, OverrideHint = OverrideHints.ForFunction(prefix + PlannerNames.Snake(s.Name) + "_get_" + PlannerNames.Snake(label), h.RustPath + "::" + label, $"pub {name}: {field.Type?.Text}") } });
                    }
                }

                // setter
                var setter = new ExportedFunction
                {
                    RustPath = h.RustPath + "::set_" + label,
                    Doc = $"Sets the field {label} of the {s.Name}.",
                    Kind = FunctionKind.MutMethod,
                    Owner = h,
                    ReturnsStatus = true,
                    OwnerRustPath = h.RustPath,
                    Return = new ReturnMapping { Shape = ReturnShape.Void, Type = TypeMappings.Unit() },
                };
                setter.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(h, "&mut " + s.Name), Role = ParameterRole.Handle });
                var setterUses = new List<HandleType> { h };
                var valueArg = FieldArgument(ty, "value", setter.Parameters, setterUses);
                if (valueArg != null && !methodNames.Contains("set_" + bare) && FunctionFilterReason(SuffixForms(s, "set_" + bare)) == null)
                {
                    setter.RustCallExpression = "{ " + access + " = " + valueArg + "; }";
                    DedupeParameterNames(setter);
                    functions.Add(new PlannedFunction { Function = setter, Forms = SuffixForms(s, "set_" + bare), OwnerType = h, Generated = "setter", Suffix = "set_" + PlannerNames.Snake(label), Uses = setterUses.Distinct().ToList() });
                }

                // constructor argument
                if (constructible)
                {
                    var arg = FieldArgument(ty, field.Name ?? "item" + name, ctor.Parameters, ctorUses);
                    if (arg == null)
                    {
                        constructible = false;
                    }
                    else
                    {
                        ctorArgs.Add(field.Name != null ? name + ": " + arg : arg);
                    }
                }
            }

            if (constructible && FunctionFilterReason(SuffixForms(s, "from_fields")) == null)
            {
                var typePath = MappingPlanner.UpstreamCrateAlias + "::" + PlannerNames.WithoutCrate(s.PrimaryPublicPath);
                ctor.RustCallExpression = s.Shape == RustItemShape.Tuple ? typePath + "(" + string.Join(", ", ctorArgs) + ")" : typePath + " { " + string.Join(", ", ctorArgs) + " }";
                ctor.Return = new ReturnMapping { Shape = ReturnShape.Handle, Type = TypeMappings.Handle(h, s.Name) };
                ctor.Parameters.Add(Out("out", "result", TypeMappings.Handle(h, s.Name), ParameterRole.OutValue));
                DedupeParameterNames(ctor);
                // "new" (a C# constructor) unless the crate has its own new
                var suffix = methodNames.Contains("new") ? "from_fields" : "new";
                functions.Add(new PlannedFunction { Function = ctor, Forms = SuffixForms(s, "from_fields"), OwnerType = h, Generated = "fields", Suffix = suffix, Uses = ctorUses.Distinct().ToList() });
            }
        }
    }

    /// <summary>A data-carrying enum object gets <c>variant()</c>: the name of the variant it
    /// holds (a match over every variant; <c>#[non_exhaustive]</c> adds a wildcard arm).</summary>
    private void AddVariantName(RustEnum e, HandleType h)
    {
        if (api.FindMethod(e, "variant") != null || e.Variants.Count == 0 || e.Variants.Any(v => v.Cfg.Count > 0) || FunctionFilterReason(SuffixForms(e, "variant")) != null)
        {
            return;
        }

        var typePath = MappingPlanner.UpstreamCrateAlias + "::" + PlannerNames.WithoutCrate(e.PrimaryPublicPath);
        var arms = e.Variants.Select(v => typePath + "::" + v.Name + (v.Shape switch { RustItemShape.Tuple => "(..)", RustItemShape.Named => " { .. }", _ => string.Empty }) + " => \"" + PlannerNames.Bare(v.Name) + "\"").ToList();
        if (e.HasAttribute("#[non_exhaustive"))
        {
            arms.Add("_ => \"?\"");
        }

        var f = new ExportedFunction
        {
            RustPath = h.RustPath + "::variant",
            Doc = $"The name of the {e.Name} variant this object holds ({string.Join(", ", e.Variants.Select(v => PlannerNames.Bare(v.Name)))}).",
            Kind = FunctionKind.Method,
            Owner = h,
            ReturnsStatus = true,
            OwnerRustPath = h.RustPath,
            Return = new ReturnMapping { Shape = ReturnShape.OwnedString, Type = TypeMappings.StringOutput(prefix, "String") },
            RustCallExpression = "::std::string::String::from(match " + MappingPlanner.ReceiverLocal + " { " + string.Join(", ", arms) + " })",
        };
        f.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(h, "&" + e.Name), Role = ParameterRole.Handle });
        f.Parameters.Add(Out("out", "result", TypeMappings.StringOutput(prefix, "String"), ParameterRole.OutBuffer));
        functions.Add(new PlannedFunction { Function = f, Forms = SuffixForms(e, "variant"), OwnerType = h, Generated = "variant", Suffix = "variant", Uses = new List<HandleType> { h } });
        AddVariantConstructors(e, h, typePath);
    }

    /// <summary>A data-carrying enum object gets one static constructor per variant whose
    /// fields all cross C (<c>Value::Scalar(f64)</c> -&gt; <c>Value.Scalar(double)</c>;
    /// object fields are cloned, so only Clone types qualify). A variant that is
    /// <c>#[non_exhaustive]</c> cannot be built outside its crate and gets none.</summary>
    private void AddVariantConstructors(RustEnum e, HandleType h, string typePath)
    {
        var methodNames = new HashSet<string>(api.MethodsOf(e).Select(m => PlannerNames.Bare(m.Name)), StringComparer.Ordinal);
        var scope = new Scope { Module = e.Module, Owner = e };
        foreach (var v in e.Variants)
        {
            var suffix = PlannerNames.Snake(v.Name);
            if (v.Attributes.Any(a => a.Replace(" ", string.Empty).StartsWith("#[non_exhaustive", StringComparison.Ordinal)) || methodNames.Contains(suffix)
                || FunctionFilterReason(SuffixForms(e, PlannerNames.Bare(v.Name))) != null)
            {
                continue;
            }

            var ctor = new ExportedFunction
            {
                RustPath = h.RustPath + "::" + PlannerNames.Bare(v.Name),
                Doc = string.IsNullOrWhiteSpace(v.Docs) ? $"Makes the {e.Name}::{PlannerNames.Bare(v.Name)} variant." : v.Docs.TrimEnd(),
                Kind = FunctionKind.Constructor,
                Owner = h,
                ReturnsStatus = true,
                OwnerRustPath = h.RustPath,
            };
            var uses = new List<HandleType> { h };
            var args = new List<string>();
            var ok = true;
            foreach (var field in v.Fields)
            {
                if (field.Cfg.Count > 0)
                {
                    ok = false;
                    break;
                }

                var local = field.Name ?? (v.Fields.Count == 1 ? "value" : "item" + field.Index.ToString(CultureInfo.InvariantCulture));
                if (PlannerNames.Bare(local) == MappingPlanner.ReceiverLocal || local.StartsWith("__cb", StringComparison.Ordinal))
                {
                    ok = false;
                    break;
                }

                var arg = FieldArgument(Classify(field.Type, scope, 0), local, ctor.Parameters, uses);
                if (arg == null)
                {
                    ok = false;
                    break;
                }

                args.Add(field.Name != null ? field.Name + ": " + arg : arg);
            }

            if (!ok)
            {
                continue;
            }

            ctor.RustCallExpression = typePath + "::" + v.Name + v.Shape switch
            {
                RustItemShape.Tuple => "(" + string.Join(", ", args) + ")",
                RustItemShape.Named => " { " + string.Join(", ", args) + " }",
                _ => string.Empty,
            };
            ctor.Return = new ReturnMapping { Shape = ReturnShape.Handle, Type = TypeMappings.Handle(h, e.Name) };
            ctor.Parameters.Add(Out("out", "result", TypeMappings.Handle(h, e.Name), ParameterRole.OutValue));
            DedupeParameterNames(ctor);
            functions.Add(new PlannedFunction { Function = ctor, Forms = SuffixForms(e, PlannerNames.Bare(v.Name)), OwnerType = h, Generated = "variant", Suffix = suffix, Uses = uses.Distinct().ToList() });
        }
    }

    /// <summary>Plans the getter of one public field; returns the reason when its type cannot
    /// be handed to C.</summary>
    private string PlanGetter(RustStruct s, HandleType h, RustField field, string label, string access, PlannedType ty, IReadOnlyList<string> forms)
    {
        // a field that is itself a shared reference (&'static str): copy what it points to
        var reference = ty.Kind == PlannedTypeKind.Reference && !ty.Mutable;
        var owned = CopyOf(reference ? ty.Inner : ty, out var single, out _, out var why);
        if (owned == null)
        {
            var bad = ty.FirstUnsupported();
            return why != null
                ? "is a public field that " + why
                : bad != null
                    ? $"is a public field whose type is {bad.Description ?? bad.Text}, so it has no getter or setter.{(bad.Advice != null ? " " + bad.Advice : string.Empty)}"
                    : $"is a public field of type {ty.Text}, whose value cannot be copied out automatically.";
        }

        var failure = MapReturn(owned, true, out var mapping, out var outs);
        if (failure != null)
        {
            return "is a public field that " + failure;
        }

        var getter = new ExportedFunction
        {
            RustPath = h.RustPath + "::" + label,
            Doc = (string.IsNullOrWhiteSpace(field.Docs) ? $"Gets the field {label} of the {s.Name}." : field.Docs.TrimEnd()) + "\n\nThe result is a copy of the field.",
            Kind = FunctionKind.Method,
            Owner = h,
            ReturnsStatus = true,
            OwnerRustPath = h.RustPath,
            Return = mapping,
            RustCallExpression = single.Replace("{0}", (reference ? string.Empty : "&") + access, StringComparison.Ordinal),
        };
        getter.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(h, "&" + s.Name), Role = ParameterRole.Handle });
        foreach (var o in outs)
        {
            getter.Parameters.Add(o);
        }

        DedupeParameterNames(getter);
        functions.Add(new PlannedFunction { Function = getter, Forms = forms.Concat(SuffixForms(s, "get_" + PlannerNames.Bare(label))).ToList(), OwnerType = h, Generated = "getter", Suffix = "get_" + PlannerNames.Snake(label), Uses = new[] { h }.Concat(owned.Handles()).Distinct().ToList() });
        return null;
    }

    /// <summary>Maps an owned field value as a parameter; returns the Rust expression that
    /// builds the field from the parameter's local, or <c>null</c> when it cannot be passed.
    /// An object field is taken by reference and cloned (only when its type is Clone).</summary>
    private string FieldArgument(PlannedType ty, string rustName, IList<ParameterMapping> parameters, List<HandleType> uses)
    {
        if (ty.Kind == PlannedTypeKind.Option && ty.Inner?.Kind == PlannedTypeKind.Handle)
        {
            if (!ty.Inner.Handle.HasClone)
            {
                return null;
            }

            parameters.Add(Param(rustName, PlannerNames.SafeC("has_" + PlannerNames.Bare(rustName)), PlannerNames.EscapeCSharp("has" + PlannerNames.Pascal(rustName)), TypeMappings.Bool(), ParameterRole.InPresent));
            parameters.Add(Param(rustName, PlannerNames.SafeC(rustName), PlannerNames.Camel(rustName), TypeMappings.Handle(ty.Inner.Handle, "&" + ty.Inner.Text), ParameterRole.Handle));
            uses.Add(ty.Inner.Handle);
            return RustLocal(rustName) + ".cloned()";
        }

        if (ty.Kind == PlannedTypeKind.Handle)
        {
            if (!ty.Handle.HasClone)
            {
                return null;
            }

            parameters.Add(Param(rustName, PlannerNames.SafeC(rustName), PlannerNames.Camel(rustName), TypeMappings.Handle(ty.Handle, "&" + ty.Text), ParameterRole.Handle));
            uses.Add(ty.Handle);
            return "::std::clone::Clone::clone(" + RustLocal(rustName) + ")";
        }

        var probe = new List<ParameterMapping>();
        if (MapParameter(ty, rustName, probe, out var arg) != null)
        {
            return null;
        }

        foreach (var p in probe)
        {
            parameters.Add(p);
        }

        uses.AddRange(ty.Handles());
        return arg == rustName ? RustLocal(rustName) : arg;
    }

    private static string RustLocal(string rustName) => rustName;

    private void AddGeneratedHandleFunctions()
    {
        foreach (var pair in handles.OrderBy(p => p.Key.Id, StringComparer.Ordinal))
        {
            var s = pair.Key;
            var h = pair.Value;
            var typePath = PlannerNames.WithoutCrate(s.PrimaryPublicPath);
            var destroy = new ExportedFunction
            {
                RustPath = h.RustPath + "::destroy",
                Doc = string.Empty,
                Kind = FunctionKind.Destroy,
                Owner = h,
                Return = new ReturnMapping { Shape = ReturnShape.Void, Type = TypeMappings.Unit() },
                ReturnsStatus = false,
                OwnerRustPath = h.RustPath,
            };
            destroy.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(h, s.Name), Role = ParameterRole.Handle });
            h.Destroy = destroy;
            functions.Add(new PlannedFunction { Function = destroy, Forms = SuffixForms(s, "destroy"), OwnerType = h, Generated = "destroy", Uses = new List<HandleType> { h } });

            if (h.HasDefault && FunctionFilterReason(SuffixForms(s, "default")) == null)
            {
                var created = new ExportedFunction
                {
                    RustPath = h.RustPath + "::default",
                    Doc = string.Empty,
                    Kind = FunctionKind.Constructor,
                    Owner = h,
                    Return = new ReturnMapping { Shape = ReturnShape.Handle, Type = TypeMappings.Handle(h, s.Name) },
                    ReturnsStatus = true,
                    OwnerRustPath = h.RustPath,
                    RustCallExpression = $"<{MappingPlanner.UpstreamCrateAlias}::{typePath} as ::core::default::Default>::default()",
                };
                created.Parameters.Add(Out("out", "result", TypeMappings.Handle(h, s.Name), ParameterRole.OutValue));
                functions.Add(new PlannedFunction { Function = created, Forms = SuffixForms(s, "default"), OwnerType = h, Generated = "default", Uses = new List<HandleType> { h } });
            }

            if (h.HasClone && FunctionFilterReason(SuffixForms(s, "clone")) == null)
            {
                var clone = new ExportedFunction
                {
                    RustPath = h.RustPath + "::clone",
                    Doc = string.Empty,
                    Kind = FunctionKind.Method,
                    Owner = h,
                    Return = new ReturnMapping { Shape = ReturnShape.Handle, Type = TypeMappings.Handle(h, s.Name) },
                    ReturnsStatus = true,
                    OwnerRustPath = h.RustPath,
                    RustCallExpression = $"<{MappingPlanner.UpstreamCrateAlias}::{typePath} as ::core::clone::Clone>::clone({MappingPlanner.ReceiverLocal})",
                };
                clone.Parameters.Add(new ParameterMapping { RustName = "self", CName = MappingPlanner.HandleParameterName, ManagedName = MappingPlanner.HandleParameterName, Type = TypeMappings.Handle(h, "&" + s.Name), Role = ParameterRole.Handle });
                clone.Parameters.Add(Out("out", "result", TypeMappings.Handle(h, s.Name), ParameterRole.OutValue));
                functions.Add(new PlannedFunction { Function = clone, Forms = SuffixForms(s, "clone"), OwnerType = h, Generated = "clone", Uses = new List<HandleType> { h } });
            }
        }
    }

    private static IReadOnlyList<string> SuffixForms(RustItem type, string suffix)
        => PathGlob.Forms(new[] { type.CanonicalPath }.Concat(type.PublicPaths).Select(p => p + "::" + suffix));

    private void AddReleaseFunctions()
    {
        bool Produces(MappingKind kind) => functions.Any(f => f.Function.Parameters.Any(p => p.Role == ParameterRole.OutBuffer && p.Type.Kind == kind));

        if (Produces(MappingKind.PrimitiveVec) || Produces(MappingKind.ValueVec))
        {
            AddRelease(MappingPlanner.FreeBufferSuffix, "FreeBuffer", MappingKind.PrimitiveVec);
        }

        if (Produces(MappingKind.String) || functions.Any(f => f.Function.Parameters.Any(p => p.Role == ParameterRole.OutBuffer && p.Type.Kind == MappingKind.ValueVec && p.Type.Element?.Kind == MappingKind.String)))
        {
            AddRelease(MappingPlanner.FreeStringSuffix, "FreeString", MappingKind.String);
        }
    }

    private void AddRelease(string suffix, string managed, MappingKind kind)
    {
        var f = new ExportedFunction
        {
            RustPath = suffix,
            CName = prefix + suffix,
            ManagedName = managed,
            Doc = string.Empty,
            Kind = FunctionKind.Release,
            Return = new ReturnMapping { Shape = ReturnShape.Void, Type = TypeMappings.Unit() },
            ReturnsStatus = false,
        };
        f.Parameters.Add(new ParameterMapping { RustName = "buffer", CName = "buffer", ManagedName = "buffer", Type = TypeMappings.Buffer(prefix, kind), Role = ParameterRole.Value });
        functions.Add(new PlannedFunction { Function = f, Forms = Array.Empty<string>(), Generated = "release", Uses = new List<HandleType>() });
    }

    private void PlanConstants()
    {
        foreach (var k in api.Constants.Where(c => c.Owner != null && c.IsReachable && !c.IsDocHidden && CfgReason(c) == null).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var typeText = k.Type?.Text ?? "?";
            var value = k.Value != null ? $" = {k.Value}" : string.Empty;
            var owner = ShortPath(k);
            owner = owner.Contains("::", StringComparison.Ordinal) ? owner.Substring(0, owner.LastIndexOf("::", StringComparison.Ordinal)) : owner;
            var reason = PathGlob.AnyMatch(wrapPlan.ExcludeFunctions, FormsOf(k))
                ? "is excluded by your WrapPlan's ExcludeFunctions."
                : $"is an associated constant of {owner} ({typeText}{value}); only free constants (outside an impl block) are copied into C# automatically. Copy the value into your C# code, or add a FacadeOverride that returns it.";
            var cReturn = k.Type != null && k.Type.Kind == RustTypeKind.Primitive && (TypeMappings.IsPrimitive(k.Type.Name) || k.Type.Name == "bool") ? k.Type.Name : null;
            Skip(k, "constant", reason, OverrideHints.ForConstant(prefix + string.Join("_", ShortPath(k).Split("::").Select(PlannerNames.Snake)).ToLowerInvariant(), ShortPath(k), cReturn));
        }

        foreach (var k in api.Constants.Where(c => c.Owner == null && c.IsReachable).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (CfgReason(k) != null || k.IsDocHidden)
            {
                continue;
            }

            var typeText = k.Type?.Text ?? "?";
            var cReturn = k.Type != null && k.Type.Kind == RustTypeKind.Primitive && (TypeMappings.IsPrimitive(k.Type.Name) || k.Type.Name == "bool") ? k.Type.Name : null;
            var value = k.Value != null ? $" = {k.Value}" : string.Empty;
            var what = k.Kind == RustConstantKind.Static ? "a static" : "a constant";
            var filter = PathGlob.AnyMatch(wrapPlan.ExcludeFunctions, FormsOf(k)) ? "is excluded by your WrapPlan's ExcludeFunctions." : null;
            var literalType = k.Type == null ? null
                : k.Type.Kind == RustTypeKind.Primitive ? k.Type.Name
                : k.Type.IsStrReference ? "&str"
                : null;
            string why = null;
            if (filter == null && literalType != null && !k.IsMutable
                && ConstantLiterals.TryConvert(literalType, k.Value, out var managedType, out var literal, out why))
            {
                constants.Add((k, new ConstantMapping { RustPath = ShortPath(k), RustType = typeText, RustValue = k.Value, ManagedType = managedType, ManagedLiteral = literal, Doc = k.Docs ?? string.Empty }));
                continue;
            }

            var reason = filter != null
                ? filter
                : k.IsMutable
                    ? $"is a static mut ({typeText}); its value can change at run time, so it is not copied into C#. {OverrideAdvice}"
                    : literalType != null && why != null
                        ? $"is {what} ({typeText}{value}), but {why}, so it is not copied into C#. Copy the value into your C# code, or add a FacadeOverride that returns it."
                        : $"is {what} of type {typeText}{value}; only constants of number, bool and &str types with a plain literal value are copied into C# automatically. Copy the value into your C# code, or add a FacadeOverride that returns it.";
            Skip(k, k.Kind == RustConstantKind.Static ? "static" : "constant", reason, OverrideHints.ForConstant(prefix + string.Join("_", ShortPath(k).Split("::").Select(PlannerNames.Snake)).ToLowerInvariant(), ShortPath(k), cReturn));
        }
    }

    // ------------------------------------------------------------------ pass-through mode

    private void PlanPassThrough(IReadOnlyList<RustFunction> exports)
    {
        foreach (var f in exports)
        {
            var gate = CfgReason(f);
            if (gate != null)
            {
                SkipPassThrough(f, gate);
                continue;
            }

            var filter = FunctionFilterReason(FormsOf(f));
            if (filter != null)
            {
                SkipPassThrough(f, filter);
                continue;
            }

            if (f.HasGenerics)
            {
                SkipPassThrough(f, $"is generic ({f.Generics.Text}), which a C export cannot be.");
                continue;
            }

            var exported = new ExportedFunction
            {
                RustPath = ShortPath(f),
                CName = PlannerNames.Bare(f.Name),
                Doc = f.Docs ?? string.Empty,
                Kind = FunctionKind.PassThrough,
                IsUnsafeUpstream = f.IsUnsafe,
                ReturnsStatus = false,
            };
            string failure = null;
            foreach (var p in f.Params)
            {
                var mapping = PassThroughMapping(p.Type, out var why);
                if (mapping == null || mapping.Kind == MappingKind.Unit)
                {
                    failure = $"takes {why ?? "an empty value (())"} in its parameter '{p.Name}', which is not declared automatically. Add a FacadeOverride with CSharpSource that declares this export.";
                    break;
                }

                exported.Parameters.Add(Param(p.Name, PlannerNames.SafeC(p.Name), PlannerNames.Camel(p.Name), mapping, ParameterRole.Value));
            }

            if (failure == null)
            {
                var returnMapping = f.ReturnType == null || f.ReturnType.IsUnit ? TypeMappings.Unit() : PassThroughMapping(f.ReturnType, out _);
                if (returnMapping == null)
                {
                    failure = $"returns {PassThroughWhy(f.ReturnType)}, which is not declared automatically. Add a FacadeOverride with CSharpSource that declares this export.";
                }
                else
                {
                    exported.Return = new ReturnMapping { Type = returnMapping, Shape = returnMapping.Kind == MappingKind.Unit ? ReturnShape.Void : ReturnShape.Value };
                }
            }

            if (failure != null)
            {
                SkipPassThrough(f, failure);
                continue;
            }

            DedupeParameterNames(exported);
            functions.Add(new PlannedFunction { Function = exported, Forms = FormsOf(f), Generated = "passthrough", Uses = new List<HandleType>() });
        }

        var exportSet = new HashSet<RustFunction>(exports);
        var rustOnly = api.Functions.Count(f => f.IsReachable && !exportSet.Contains(f)) + api.Structs.Count(s => s.IsReachable) + api.Enums.Count(e => e.IsReachable);
        if (rustOnly > 0)
        {
            warnings.Add($"The crate already exports C functions, so they are used as they are; its {rustOnly.ToString(CultureInfo.InvariantCulture)} other public Rust items (functions and types) cannot be called through the built library and are not wrapped.");
        }
    }

    private void SkipPassThrough(RustFunction f, string reason)
        => Skip(f, "function", reason, OverrideHints.ForPassThrough(plan.LibName, PlannerNames.Bare(f.Name), f.Signature));

    private string PassThroughWhy(RustType type)
    {
        PassThroughMapping(type, out var why);
        return why ?? type?.Text;
    }

    private TypeMapping PassThroughMapping(RustType type, out string why)
    {
        why = null;
        if (type == null)
        {
            return null;
        }

        switch (type.Kind)
        {
            case RustTypeKind.Unit:
                return TypeMappings.Unit();
            case RustTypeKind.Primitive:
                if (type.Name == "bool")
                {
                    return TypeMappings.Bool();
                }

                if (TypeMappings.IsPrimitive(type.Name))
                {
                    return TypeMappings.Primitive(type.Name, type.Text);
                }

                why = $"a {type.Text} value";
                return null;
            case RustTypeKind.Ptr:
            {
                var inner = type.Inner;
                string pointee = null;
                if (inner != null && inner.Kind == RustTypeKind.Primitive && TypeMappings.IsPrimitive(inner.Name))
                {
                    pointee = inner.Name;
                }
                else if (inner != null && inner.Kind == RustTypeKind.Path && inner.PathName != null && TypeMappings.FixedCAliases.TryGetValue(inner.PathName, out var alias))
                {
                    pointee = alias;
                }

                return TypeMappings.RawPointer(pointee, type.Mutable == true, type.Text);
            }

            case RustTypeKind.Path:
            {
                var name = type.PathName;
                if (type.Segments.Count == 1 || StdRoots.Contains(type.Segments[0].Name) || type.Segments[0].Name == "libc")
                {
                    if (name == "c_long" || name == "c_ulong")
                    {
                        return TypeMappings.CLong(name == "c_ulong", type.Text);
                    }

                    if (name == "c_char")
                    {
                        return TypeMappings.CChar(type.Text);
                    }

                    if (name != null && TypeMappings.FixedCAliases.TryGetValue(name, out var prim))
                    {
                        return TypeMappings.Primitive(prim, type.Text);
                    }
                }

                var local = ResolveLocal(type, new Scope { Module = "crate" });
                why = local != null
                    ? $"the Rust type {type.Text} (in pass-through mode only C numbers and pointers are declared automatically)"
                    : $"{type.Text}, a type CrateBuilder does not declare automatically";
                return null;
            }

            case RustTypeKind.FnPtr:
                why = $"a callback ({type.Text})";
                return null;
            default:
                why = type.Text;
                return null;
        }
    }

    // ------------------------------------------------------------------ type classification

    private PlannedType Classify(RustType t, Scope scope, int depth)
    {
        if (t == null)
        {
            return PlannedType.Unsupported("?", "a type the extractor could not read");
        }

        var text = t.Text ?? t.ToString();
        if (depth > 16)
        {
            return PlannedType.Unsupported(text, $"a type nested too deeply ({text})");
        }

        switch (t.Kind)
        {
            case RustTypeKind.Unit:
                return new PlannedType { Kind = PlannedTypeKind.Unit, Text = text };
            case RustTypeKind.Primitive:
                return ClassifyPrimitive(t.Name, text);
            case RustTypeKind.Reference:
                return Wrap(PlannedTypeKind.Reference, text, Classify(t.Inner, scope, depth + 1), t.Mutable == true);
            case RustTypeKind.Ptr:
                return Wrap(PlannedTypeKind.Pointer, text, Classify(t.Inner, scope, depth + 1), t.Mutable == true);
            case RustTypeKind.Slice:
                return Wrap(PlannedTypeKind.Slice, text, Classify(t.Elem, scope, depth + 1), false);
            case RustTypeKind.Array:
                if (t.LenValue == null || t.LenValue.Value == 0 || t.LenValue.Value > int.MaxValue)
                {
                    return PlannedType.Unsupported(text, $"an array whose length is not a plain positive number ({text})");
                }

                var array = Wrap(PlannedTypeKind.Array, text, Classify(t.Elem, scope, depth + 1), false);
                array.Length = (int)t.LenValue.Value;
                return array;
            case RustTypeKind.Tuple:
            {
                var tuple = new PlannedType { Kind = PlannedTypeKind.Tuple, Text = text };
                foreach (var e in t.Elems ?? new List<RustType>())
                {
                    tuple.Items.Add(Classify(e, scope, depth + 1));
                }

                return tuple;
            }

            case RustTypeKind.Path:
                return ClassifyPath(t, text, scope, depth);
            case RustTypeKind.ImplTrait:
                return ClassifyTraitBound(t.Bounds, text, true);
            case RustTypeKind.DynTrait:
                return ClassifyTraitBound(t.Bounds, text, false);
            case RustTypeKind.FnPtr:
                return PlannedType.Unsupported(text, $"a callback ({text})");
            case RustTypeKind.Never:
                return PlannedType.Unsupported(text, "the never type (!), so it never returns normally");
            default:
                return PlannedType.Unsupported(text, $"a type CrateBuilder cannot read ({text})");
        }
    }

    private static PlannedType Wrap(PlannedTypeKind kind, string text, PlannedType inner, bool mutable)
        => new PlannedType { Kind = kind, Text = text, Inner = inner, Mutable = mutable };

    private static PlannedType ClassifyPrimitive(string name, string text)
    {
        switch (name)
        {
            case "bool":
                return new PlannedType { Kind = PlannedTypeKind.Bool, Text = text };
            case "str":
                return new PlannedType { Kind = PlannedTypeKind.Str, Text = text };
            case "char":
                return PlannedType.Unsupported(text, $"a char ({text})", "A Rust char has no direct C equivalent; an override can pass it as a u32.");
            case "i128":
            case "u128":
            case "f16":
            case "f128":
                return PlannedType.Unsupported(text, $"a {name} number ({text})", "C has no portable equivalent; an override can split it into smaller numbers.");
            default:
                return TypeMappings.IsPrimitive(name)
                    ? new PlannedType { Kind = PlannedTypeKind.Primitive, Text = text, Primitive = name }
                    : PlannedType.Unsupported(text, $"a {text} value");
        }
    }

    private static PlannedType ClassifyTraitBound(IList<string> bounds, string text, bool isImpl)
    {
        var list = bounds ?? new List<string>();
        if (list.Any(b => b.StartsWith("Fn(", StringComparison.Ordinal) || b.StartsWith("FnMut(", StringComparison.Ordinal) || b.StartsWith("FnOnce(", StringComparison.Ordinal) || b.Contains("::Fn", StringComparison.Ordinal)))
        {
            return PlannedType.Unsupported(text, $"a callback ({text})");
        }

        if (list.Any(b => b.StartsWith("Iterator", StringComparison.Ordinal) || b.StartsWith("IntoIterator", StringComparison.Ordinal) || b.StartsWith("DoubleEndedIterator", StringComparison.Ordinal) || b.StartsWith("ExactSizeIterator", StringComparison.Ordinal)))
        {
            return PlannedType.Unsupported(text, $"an iterator ({text})", "An override can collect the items into a Vec first.");
        }

        if (list.Any(b => b.StartsWith("Future", StringComparison.Ordinal) || b.Contains("::Future", StringComparison.Ordinal)))
        {
            return PlannedType.Unsupported(text, $"a future ({text})");
        }

        if (isImpl)
        {
            var traits = string.Join(" + ", list.Where(b => !b.StartsWith('\'')));
            return PlannedType.Unsupported(text, $"\"any type that implements {traits}\" ({text})", "A C function needs one concrete type.");
        }

        return PlannedType.Unsupported(text, $"a trait object ({text})", "Its concrete type is chosen at run time, so there is no fixed C layout or list of functions to generate for it.");
    }

    private PlannedType ClassifyPath(RustType t, string text, Scope scope, int depth)
    {
        if (t.Qself != null)
        {
            return PlannedType.Unsupported(text, $"an associated type ({text})");
        }

        if (t.Segments == null || t.Segments.Count == 0)
        {
            return PlannedType.Unsupported(text, $"a type CrateBuilder cannot read ({text})");
        }

        if (t.IsSelf)
        {
            return scope.Owner != null ? FromItem(scope.Owner, t, text, scope, depth) : PlannedType.Unsupported(text, "Self outside of a type");
        }

        var last = t.LastSegment;
        if (t.Segments.Count == 1 && last.Args.Count == 0 && scope.Substitutions != null && scope.Substitutions.TryGetValue(last.Name, out var substitute))
        {
            return Classify(substitute.Type, substitute.Scope, depth + 1);
        }

        var local = ResolveLocal(t, scope);
        if (local != null)
        {
            return FromItem(local, t, text, scope, depth);
        }

        var args = last.Args;
        PlannedType Arg(int i) => i < args.Count ? Classify(args[i], scope, depth + 1) : PlannedType.Unsupported(text, $"{text} without its type arguments");

        switch (last.Name)
        {
            case "String":
                return new PlannedType { Kind = PlannedTypeKind.String, Text = text };
            case "Vec":
                return Wrap(PlannedTypeKind.Vec, text, Arg(0), false);
            case "Option":
                return Wrap(PlannedTypeKind.Option, text, Arg(0), false);
            case "Result":
            {
                var result = Wrap(PlannedTypeKind.Result, text, Arg(0), false);
                result.ErrorType = args.Count > 1 ? args[1] : null;
                result.ErrorItem = args.Count > 1 && args[1].Kind == RustTypeKind.Path ? ResolveLocal(args[1], scope) : null;
                return result;
            }

            case "Box":
            {
                var inner = Arg(0);
                return inner.Kind == PlannedTypeKind.Unsupported ? inner : PlannedType.Unsupported(text, $"a boxed value ({text})", "An override can unbox it.");
            }

            case "Rc":
            case "Arc":
            case "Weak":
                return PlannedType.Unsupported(text, $"shared ownership ({text})", "A wrapped object owns its value outright, so a shared value would have to be copied out of the Arc/Rc, and then changes made through the other owners would no longer be seen; that changes what the code means, so it is never done automatically.");
            case "RefCell":
            case "Cell":
            case "Mutex":
            case "RwLock":
            case "OnceCell":
            case "OnceLock":
            case "UnsafeCell":
                return PlannedType.Unsupported(text, $"interior mutability ({text})");
            case "HashMap":
            case "BTreeMap":
            case "HashSet":
            case "BTreeSet":
            case "IndexMap":
            case "IndexSet":
                return PlannedType.Unsupported(text, $"a map or set ({text})");
            case "VecDeque":
            case "LinkedList":
            case "BinaryHeap":
                return PlannedType.Unsupported(text, $"a collection ({text})");
            case "Cow":
                return PlannedType.Unsupported(text, $"a borrowed-or-owned value ({text})");
            case "PathBuf":
            case "OsString":
                // Crosses C as UTF-8 text: an argument is built from the string, a result is
                // checked to be UTF-8 (a path that is not fails with an error, never lossy).
                return new PlannedType { Kind = PlannedTypeKind.String, Text = text, StdText = last.Name };
            case "Path":
            case "OsStr":
                return new PlannedType { Kind = PlannedTypeKind.Str, Text = text, StdText = last.Name };
            case "CString":
            case "CStr":
                return PlannedType.Unsupported(text, $"a C string ({text})", "An override can convert it to or from a String.");
            case "Pin":
            case "Future":
                return PlannedType.Unsupported(text, $"a future ({text})");
            case "Duration":
            case "Instant":
            case "SystemTime":
                return PlannedType.Unsupported(text, $"a time value of the Rust standard library ({text})", "An override can pass it as a number (for example seconds as f64).");
            case "PhantomData":
                return PlannedType.Unsupported(text, $"a marker type ({text})");
        }

        if (TypeMappings.FixedCAliases.TryGetValue(last.Name, out var prim))
        {
            return new PlannedType { Kind = PlannedTypeKind.Primitive, Text = text, Primitive = prim };
        }

        return PlannedType.Unsupported(text, $"{text}, a type from another crate that CrateBuilder cannot look into", "CrateBuilder reads only this crate's source, so it cannot know that type's fields, layout or functions; wrap that crate too, or add an override that converts it.");
    }

    private PlannedType FromItem(RustItem item, RustType t, string text, Scope scope, int depth)
    {
        switch (item)
        {
            case RustStruct s:
                Decide(s);
                if (mirrors.TryGetValue(s, out var mirror))
                {
                    return new PlannedType { Kind = PlannedTypeKind.Mirror, Text = text, Mirror = mirror };
                }

                if (handles.TryGetValue(s, out var handle))
                {
                    return new PlannedType { Kind = PlannedTypeKind.Handle, Text = text, Handle = handle };
                }

                return PlannedType.Unsupported(text, Blocked(s));
            case RustEnum e:
                if (handles.TryGetValue(e, out var enumHandle))
                {
                    return new PlannedType { Kind = PlannedTypeKind.Handle, Text = text, Handle = enumHandle };
                }

                return enums.TryGetValue(e, out var mapping)
                    ? new PlannedType { Kind = PlannedTypeKind.Enum, Text = text, Enum = mapping }
                    : PlannedType.Unsupported(text, Blocked(e));
            case RustTypeAlias alias:
            {
                if (alias.Type == null || depth > 8)
                {
                    return PlannedType.Unsupported(text, $"the type alias {alias.Name}, which CrateBuilder cannot resolve");
                }

                Dictionary<string, Substitution> map = null;
                var parameters = alias.Generics?.TypeParams ?? new List<RustTypeParam>();
                if (parameters.Count > 0)
                {
                    var args = t.LastSegment?.Args ?? new List<RustType>();
                    if (args.Count < parameters.Count)
                    {
                        return PlannedType.Unsupported(text, $"{text}, a generic alias used without all of its type arguments");
                    }

                    map = new Dictionary<string, Substitution>(StringComparer.Ordinal);
                    for (var i = 0; i < parameters.Count; i++)
                    {
                        map[parameters[i].Name] = new Substitution { Type = args[i], Scope = scope };
                    }
                }

                var resolved = Classify(alias.Type, new Scope { Module = alias.Module, Owner = scope.Owner, Substitutions = map }, depth + 1);
                if (resolved.Kind == PlannedTypeKind.Result && resolved.ErrorItem == null && resolved.ErrorType != null)
                {
                    resolved.ErrorItem = resolved.ErrorType.Kind == RustTypeKind.Path ? ResolveLocal(resolved.ErrorType, new Scope { Module = alias.Module }) : null;
                }

                resolved.Text = text;
                return resolved;
            }

            default:
                return PlannedType.Unsupported(text, $"{item.Name}, which is not a type");
        }
    }

    private string Blocked(RustItem item)
    {
        if (blockedTypes.TryGetValue(item, out var phrase))
        {
            return phrase;
        }

        return item.IsReachable ? $"{item.Name}, which is not wrapped" : $"{item.Name}, which is not part of the crate's public API";
    }

    private RustItem ResolveLocal(RustType t, Scope scope)
    {
        if (t == null || t.Kind != RustTypeKind.Path || t.Qself != null || t.Segments == null || t.Segments.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(t.Resolved) && typeIndex.TryGetValue(t.Resolved, out var resolved))
        {
            return resolved;
        }

        if (t.External == true)
        {
            return null;
        }

        // Nothing of that name is declared or imported where the type was written: from the
        // 2018 edition on it is a prelude / std type or a generic parameter, never an item
        // elsewhere in the crate (in 2015 a use path is crate-relative, so the old lookup stays).
        if (t.Unresolved == true && !string.Equals(api.Crate?.Edition, "2015", StringComparison.Ordinal))
        {
            return null;
        }

        var names = t.Segments.Select(s => s.Name).ToList();
        if (StdRoots.Contains(names[0]))
        {
            return null;
        }

        var module = string.IsNullOrEmpty(scope.Module) ? "crate" : scope.Module;
        var candidates = new List<string>();
        if (names[0] == "crate")
        {
            candidates.Add(string.Join("::", names));
        }
        else if (names[0] == "self")
        {
            candidates.Add(module + "::" + string.Join("::", names.Skip(1)));
        }
        else if (names[0] == "super")
        {
            var i = 0;
            while (i < names.Count && names[i] == "super")
            {
                module = Parent(module);
                i++;
            }

            candidates.Add(module + "::" + string.Join("::", names.Skip(i)));
        }
        else
        {
            var joined = string.Join("::", names);
            candidates.Add(module + "::" + joined);
            candidates.Add("crate::" + joined);
        }

        foreach (var candidate in candidates)
        {
            if (typeIndex.TryGetValue(candidate, out var found))
            {
                return found;
            }
        }

        if (names.Count == 1)
        {
            var byName = api.Structs.Cast<RustItem>().Concat(api.Enums).Concat(api.TypeAliases).Where(i => i.Name == names[0]).ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

        return null;
    }

    private static string Parent(string module)
    {
        var index = module.LastIndexOf("::", StringComparison.Ordinal);
        return index > 0 ? module.Substring(0, index) : "crate";
    }

    private TypeMapping ValueMapping(PlannedType ty)
    {
        switch (ty.Kind)
        {
            case PlannedTypeKind.Primitive:
                return TypeMappings.Primitive(ty.Primitive, ty.Text);
            case PlannedTypeKind.Bool:
                return TypeMappings.Bool();
            case PlannedTypeKind.Enum:
                return TypeMappings.Enum(ty.Enum, ty.Text);
            case PlannedTypeKind.Mirror:
                return TypeMappings.Mirror(ty.Mirror, ty.Text);
            case PlannedTypeKind.Array:
            {
                var lengths = new List<int>();
                var current = ty;
                while (current.Kind == PlannedTypeKind.Array)
                {
                    lengths.Add(current.Length);
                    current = current.Inner;
                }

                return current.Kind == PlannedTypeKind.Primitive ? TypeMappings.FixedArray(prefix, current.Primitive, lengths, ty.Text) : null;
            }

            default:
                return null;
        }
    }

    // ------------------------------------------------------------------ overrides

    private void ApplyOverrides()
    {
        foreach (var o in (wrapPlan.Overrides ?? new List<FacadeOverride>()).Where(o => o != null && !string.IsNullOrWhiteSpace(o.RustPath)).OrderBy(o => o.RustPath.Trim(), StringComparer.Ordinal))
        {
            var path = o.RustPath.Trim();
            var forms = PathGlob.Forms(new[] { path });
            var removedSkips = skipped.RemoveAll(e => e.Forms.Any(f => forms.Contains(f, StringComparer.Ordinal)));
            var removedFunctions = functions.RemoveAll(pf => pf.Function.Kind != FunctionKind.Destroy && pf.Function.Kind != FunctionKind.Release && pf.Forms.Any(f => forms.Contains(f, StringComparer.Ordinal)));
            if (removedSkips + removedFunctions == 0)
            {
                warnings.Add($"The FacadeOverride for '{path}' matches no item of the crate; its source is added anyway.");
            }

            if (plan.UpstreamIsCdylib && !string.IsNullOrWhiteSpace(o.RustSource))
            {
                warnings.Add($"The FacadeOverride for '{path}' has Rust source, but the crate already exports C functions and no facade crate is generated, so only its C# source is used.");
            }

            plan.Overrides.Add(new FacadeOverrideEntry { RustPath = path, RustSource = o.RustSource, CSharpSource = o.CSharpSource });
        }
    }

    // ------------------------------------------------------------------ naming

    private string Rename(IReadOnlyCollection<string> forms)
    {
        foreach (var rule in renames)
        {
            if (rule.Forms.Any(f => forms.Contains(f, StringComparer.Ordinal)))
            {
                rule.Used = true;
                return rule.Value;
            }
        }

        return null;
    }

    private void AssignNames()
    {
        var takenC = new HashSet<string>(StringComparer.Ordinal)
        {
            prefix + "buffer",
            prefix + "str_ref",
            prefix + MappingPlanner.LastErrorFunctionSuffix,
            prefix + MappingPlanner.FreeBufferSuffix,
            prefix + MappingPlanner.FreeStringSuffix,
            prefix + "status",
        };
        var takenTypes = new HashSet<string>(StringComparer.Ordinal) { libClass, libClass + "Exception", libClass + "Native", "Buffer" };

        // One C namespace: the facade's header declares the fixed names above, the status
        // macros, the fixed-array structs, every type, every enum constant and every function,
        // so all of them are de-duplicated together, in this deterministic order.
        var upperPrefix = prefix.ToUpperInvariant();
        foreach (var status in new[] { "STATUS_OK", "STATUS_NULL_ARGUMENT", "STATUS_INVALID_ARGUMENT", "STATUS_UPSTREAM_ERROR", "STATUS_PANIC", "STATUS_HANDLE_CONSUMED" })
        {
            takenC.Add(upperPrefix + status);
        }

        foreach (var mapping in AllMappings().Where(m => m.Kind == MappingKind.FixedArray))
        {
            takenC.Add(mapping.CType);
        }

        // types
        var types = handles.Values.Cast<object>().Concat(mirrors.Values).Concat(enums.Values)
            .OrderBy(TypePath, StringComparer.Ordinal).ThenBy(TypeRustName, StringComparer.Ordinal).ToList();
        foreach (var type in types)
        {
            var source = typeSources[type];
            var forms = FormsOf(source);
            var cName = PlannerNames.Unique(prefix + PlannerNames.Snake(source.Name), takenC, "_");
            var managed = PlannerNames.Unique(Rename(forms) ?? PlannerNames.Pascal(source.Name), takenTypes, string.Empty);
            switch (type)
            {
                case HandleType h:
                    h.CName = cName;
                    h.ManagedName = managed;
                    break;
                case MirrorStruct m:
                    m.CName = cName;
                    m.ManagedName = managed;
                    NameFields(m, source);
                    break;
                case EnumMapping e:
                    e.CName = cName;
                    e.ManagedName = managed;
                    break;
            }
        }

        // enum constants after every type name is fixed (types ordered by path)
        foreach (var e in types.OfType<EnumMapping>())
        {
            NameVariants(e, typeSources[e], takenC);
        }

        // functions: generated names first, then the rest by (base, path)
        var ordered = functions
            .Select(pf => (Planned: pf, Base: BaseCName(pf)))
            .OrderBy(x => x.Planned.Generated != null && x.Planned.Generated != "passthrough" ? 0 : 1)
            .ThenBy(x => x.Base, StringComparer.Ordinal)
            .ThenBy(x => x.Planned.Function.RustPath, StringComparer.Ordinal)
            .ToList();
        var scopes = new Dictionary<object, HashSet<string>>();
        var freeScope = new HashSet<string>(StringComparer.Ordinal) { libClass };
        var releaseScope = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (planned, baseName) in ordered)
        {
            var f = planned.Function;
            if (f.Kind == FunctionKind.PassThrough)
            {
                takenC.Add(f.CName);
            }
            else if (f.Kind != FunctionKind.Release)
            {
                f.CName = PlannerNames.Unique(baseName, takenC, "_");
            }

            if (f.Kind == FunctionKind.Release)
            {
                f.ManagedName = PlannerNames.Unique(f.ManagedName, releaseScope, string.Empty);
                continue;
            }

            HashSet<string> scope;
            string className;
            if (f.Owner != null)
            {
                if (!scopes.TryGetValue(f.Owner, out scope))
                {
                    scope = new HashSet<string>(StringComparer.Ordinal);
                    scopes[f.Owner] = scope;
                }

                className = f.Owner.ManagedName;
            }
            else
            {
                scope = freeScope;
                className = libClass;
            }

            var managed = Rename(planned.Forms) ?? DefaultManagedName(planned);
            if (managed == className)
            {
                managed += "Function";
            }

            f.ManagedName = PlannerNames.Unique(managed, scope, string.Empty);
        }
    }

    /// <summary>The C# name of a constant: SCREAMING_CASE becomes PascalCase
    /// (<c>MAX_SIZE</c> -&gt; <c>MaxSize</c>), other names are PascalCased as usual.</summary>
    internal static string ConstantName(string rustName)
    {
        var bare = PlannerNames.Bare(rustName) ?? string.Empty;
        var screaming = bare.Any(char.IsLetter) && !bare.Any(char.IsLower);
        return PlannerNames.Pascal(screaming ? bare.ToLowerInvariant() : bare);
    }

    private static string TypePath(object type) => type switch
    {
        HandleType h => h.RustPath,
        MirrorStruct m => m.RustPath,
        EnumMapping e => e.RustPath,
        _ => string.Empty,
    };

    private static string TypeRustName(object type) => type switch
    {
        HandleType h => h.RustTypeName,
        MirrorStruct m => m.RustTypeName,
        EnumMapping e => e.RustTypeName,
        _ => string.Empty,
    };

    private void NameFields(MirrorStruct mirror, RustItem source)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var cTaken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < mirror.Fields.Count; i++)
        {
            var field = mirror.Fields[i];
            var forms = PathGlob.Forms(new[] { source.CanonicalPath }.Concat(source.PublicPaths).Select(p => p + "::" + field.Name));
            var name = Rename(forms) ?? (mirror.IsTupleStruct ? "Item" + i.ToString(CultureInfo.InvariantCulture) : PlannerNames.Pascal(field.Name));
            if (name == mirror.ManagedName)
            {
                name += "Value";
            }

            field.ManagedName = PlannerNames.Unique(name, taken, string.Empty);
            field.CName = PlannerNames.Unique(field.CName, cTaken, "_");
        }
    }

    private void NameVariants(EnumMapping mapping, RustItem source, HashSet<string> cTaken)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in mapping.Variants)
        {
            var forms = PathGlob.Forms(new[] { source.CanonicalPath }.Concat(source.PublicPaths).Select(p => p + "::" + v.RustName));
            var name = Rename(forms) ?? PlannerNames.Pascal(v.RustName);
            if (name == mapping.ManagedName)
            {
                name += "Value";
            }

            v.ManagedName = PlannerNames.Unique(name, taken, string.Empty);
            v.CName = PlannerNames.Unique((mapping.CName + "_" + PlannerNames.Snake(v.RustName)).ToUpperInvariant(), cTaken, "_");
        }
    }

    private string BaseCName(PlannedFunction planned)
    {
        var f = planned.Function;
        switch (f.Kind)
        {
            case FunctionKind.PassThrough:
            case FunctionKind.Release:
                return f.CName;
            case FunctionKind.Destroy:
                return f.Owner.CName + "_destroy";
        }

        if (planned.Generated == "default")
        {
            return f.Owner.CName + "_default";
        }

        if (planned.Generated == "clone")
        {
            return f.Owner.CName + "_clone";
        }

        var ownerCName = planned.OwnerType switch
        {
            HandleType h => h.CName,
            MirrorStruct m => m.CName,
            EnumMapping e => e.CName,
            _ => null,
        };
        return ownerCName != null
            ? ownerCName + "_" + planned.Suffix
            : prefix + string.Join("_", planned.FreePath.Split("::").Select(PlannerNames.Snake));
    }

    private static string DefaultManagedName(PlannedFunction planned)
    {
        switch (planned.Generated)
        {
            case "destroy":
                return "Destroy";
            case "default":
                return "CreateDefault";
            case "clone":
                return "Clone";
            case "passthrough":
                return PlannerNames.Pascal(planned.Function.CName);
        }

        var f = planned.Function;
        var name = PlannerNames.Pascal(planned.Suffix);
        if (f.Owner == null && planned.OwnerType != null)
        {
            var owner = planned.OwnerType switch
            {
                HandleType h => h.ManagedName,
                MirrorStruct m => m.ManagedName,
                EnumMapping e => e.ManagedName,
                _ => string.Empty,
            };
            return owner + name;
        }

        return name;
    }

    // ------------------------------------------------------------------ assembly

    private IEnumerable<TypeMapping> AllMappings()
    {
        IEnumerable<TypeMapping> Chain(TypeMapping m)
        {
            for (var x = m; x != null; x = x.Element)
            {
                yield return x;
            }
        }

        foreach (var m in mirrors.Values)
        {
            foreach (var field in m.Fields)
            {
                foreach (var x in Chain(field.Type))
                {
                    yield return x;
                }
            }
        }

        foreach (var pf in functions)
        {
            foreach (var p in pf.Function.Parameters)
            {
                foreach (var x in Chain(p.Type))
                {
                    yield return x;
                }
            }

            if (pf.Function.Return != null)
            {
                foreach (var x in Chain(pf.Function.Return.Type))
                {
                    yield return x;
                }

                foreach (var item in pf.Function.Return.TupleItems)
                {
                    foreach (var x in Chain(item))
                    {
                        yield return x;
                    }
                }
            }
        }
    }

    private void Assemble()
    {
        foreach (var mapping in AllMappings().ToList())
        {
            TypeMappings.Finish(mapping);
        }

        foreach (var f in functions.Select(pf => pf.Function).OrderBy(f => f.CName, StringComparer.Ordinal))
        {
            plan.Functions.Add(f);
        }

        foreach (var h in handles.Values.OrderBy(h => h.RustPath, StringComparer.Ordinal).ThenBy(h => h.CName, StringComparer.Ordinal))
        {
            foreach (var f in plan.Functions.Where(f => f.Owner == h))
            {
                h.Methods.Add(f);
            }

            plan.Handles.Add(h);
        }

        foreach (var m in OrderMirrors())
        {
            plan.Mirrors.Add(m);
        }

        foreach (var e in enums.Values.OrderBy(e => e.RustPath, StringComparer.Ordinal).ThenBy(e => e.CName, StringComparer.Ordinal))
        {
            plan.Enums.Add(e);
        }

        var constantNames = new HashSet<string>(StringComparer.Ordinal) { libClass };
        foreach (var (item, mapping) in constants.OrderBy(c => c.Mapping.RustPath, StringComparer.Ordinal))
        {
            mapping.ManagedName = PlannerNames.Unique(Rename(FormsOf(item)) ?? ConstantName(item.Name), constantNames, string.Empty);
            plan.Constants.Add(mapping);
        }

        foreach (var s in skipped.Select(e => e.Item).OrderBy(s => s.RustPath, StringComparer.Ordinal).ThenBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Reason, StringComparer.Ordinal))
        {
            plan.Skipped.Add(s);
        }

        foreach (var rule in renames.Where(r => !r.Used))
        {
            warnings.Add($"The rename of '{rule.Key}' matches no wrapped item.");
        }

        foreach (var w in warnings)
        {
            plan.Warnings.Add(w);
        }

        foreach (var note in CollectDocNotes())
        {
            plan.DocNotes.Add(note);
        }
    }

    private IEnumerable<MirrorStruct> OrderMirrors()
    {
        var result = new List<MirrorStruct>();
        var done = new HashSet<MirrorStruct>();

        void Visit(MirrorStruct m)
        {
            if (!done.Add(m))
            {
                return;
            }

            foreach (var dependency in m.Fields.Select(f => f.Type.Mirror).Where(d => d != null).OrderBy(d => d.RustPath, StringComparer.Ordinal))
            {
                Visit(dependency);
            }

            result.Add(m);
        }

        foreach (var m in mirrors.Values.OrderBy(m => m.RustPath, StringComparer.Ordinal).ThenBy(m => m.CName, StringComparer.Ordinal))
        {
            Visit(m);
        }

        return result;
    }

    private IEnumerable<DocNote> CollectDocNotes()
    {
        var notes = new List<DocNote>();

        void Add(string path, string docs)
        {
            foreach (var sentence in DocNoteFinder.Find(docs))
            {
                if (!notes.Any(n => n.RustPath == path && n.Text == sentence))
                {
                    notes.Add(new DocNote { RustPath = path, Text = sentence });
                }
            }
        }

        foreach (var h in plan.Handles)
        {
            Add(h.RustPath, h.Doc);
        }

        foreach (var m in plan.Mirrors)
        {
            Add(m.RustPath, m.Doc);
            foreach (var field in m.Fields)
            {
                Add(m.RustPath + "::" + field.Name, field.Doc);
            }
        }

        foreach (var e in plan.Enums)
        {
            Add(e.RustPath, e.Doc);
            foreach (var v in e.Variants)
            {
                Add(e.RustPath + "::" + v.RustName, v.Doc);
            }
        }

        foreach (var f in plan.Functions.Where(f => !string.IsNullOrEmpty(f.Doc)))
        {
            Add(f.RustPath, f.Doc);
        }

        if (!plan.UpstreamIsCdylib)
        {
            foreach (var k in api.Constants.Where(c => c.Owner == null && c.IsReachable && !c.IsDocHidden && CfgReason(c) == null))
            {
                Add(ShortPath(k), k.Docs);
            }
        }

        return notes.Select((n, i) => (Note: n, Index: i)).OrderBy(x => x.Note.RustPath, StringComparer.Ordinal).ThenBy(x => x.Index).Select(x => x.Note);
    }

    // ------------------------------------------------------------------ helper types

    private sealed class Scope
    {
        public string Module { get; set; }

        public RustItem Owner { get; set; }

        public Dictionary<string, Substitution> Substitutions { get; set; }
    }

    private sealed class Substitution
    {
        public RustType Type { get; set; }

        public Scope Scope { get; set; }
    }

    private sealed class SkipEntry
    {
        public SkippedItem Item { get; set; }

        public IReadOnlyList<string> Forms { get; set; }
    }

    private sealed class PlannedFunction
    {
        public ExportedFunction Function { get; set; }

        public IReadOnlyList<string> Forms { get; set; }

        public object OwnerType { get; set; }

        public string Suffix { get; set; }

        public string FreePath { get; set; }

        public string Generated { get; set; }

        public List<HandleType> Uses { get; set; }
    }

    private sealed class RenameRule
    {
        public string Key { get; set; }

        public string Value { get; set; }

        public IReadOnlyList<string> Forms { get; set; }

        public bool Used { get; set; }
    }
}
