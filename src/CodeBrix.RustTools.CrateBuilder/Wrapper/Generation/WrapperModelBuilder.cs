using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// Builds the <see cref="WrapperModel"/> the templates render, from the mapping plan, the
/// wrapper settings and the compiled targets. Deterministic: the plan's order is kept and
/// every derived collection is ordinal-sorted.
/// </summary>
internal static class WrapperModelBuilder
{
    private static readonly string[] HandleReserved = { "Dispose", "AcquireNativeHandle", "ReleaseNativeHandle", "FromNative", "FromNativeHandle" };

    /// <summary>Builds the model.</summary>
    /// <exception cref="WrapperGenerationException">The plan has a shape the wrapper cannot
    /// express.</exception>
    public static WrapperModel Build(WrapperInput input)
    {
        var plan = input.Plan ?? throw new WrapperGenerationException("No mapping plan was given to the wrapper writer.");
        var options = input.Options;
        if (string.IsNullOrWhiteSpace(plan.LibName))
        {
            throw new WrapperGenerationException("The mapping plan has no LibName (the native library name).");
        }

        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in plan.Handles.Select(h => h.ManagedName).Concat(plan.Mirrors.Select(m => m.ManagedName)).Concat(plan.Enums.Select(e => e.ManagedName)))
        {
            typeNames.Add(name);
        }

        var libClass = CSharpNames.Pascal(plan.LibName);
        string libClassNote = null;
        if (typeNames.Contains(libClass) || libClass == CSharpNames.LastSegment(options.RootNamespace.Replace(".", "::")))
        {
            libClassNote = typeNames.Contains(libClass)
                ? $"The entry class is {libClass}Library, not {libClass}: the crate has a type named {libClass}, which keeps that name."
                : $"The entry class is {libClass}Library, not {libClass}: the root namespace ends in {libClass}, and a class with the namespace's own name could not be used unqualified.";
            libClass += "Library";
        }

        var prefix = string.IsNullOrEmpty(plan.SymbolPrefix) ? plan.LibName + "_" : plan.SymbolPrefix;
        var model = new WrapperModel
        {
            AssemblyName = options.AssemblyName.Trim(),
            RootNamespace = options.RootNamespace.Trim(),
            LibName = plan.LibName,
            LibClassName = libClass,
            LibClassNote = libClassNote,
            NativeClassName = libClass + "Native",
            ExceptionClassName = libClass + "Exception",
            CrateName = input.CrateName ?? plan.CrateName ?? plan.LibName,
            CrateVersion = input.CrateVersion ?? "0.0.0",
            CrateLicense = input.CrateLicense,
            SourceDescription = input.SourceDescription,
            IsPassThrough = plan.UpstreamIsCdylib,
            LinksWebGpu = input.LinkToCodeBrixWebGpu || plan.LinkToCodeBrixWebGpu,
            WebGpuPackageVersion = input.WebGpuPackageVersion,
            HasSmokeTests = options.GenerateSmokeTests,
            LastErrorFunction = prefix + "last_error_message",
            FreeBufferFunction = ReleaseName(plan, "free_buffer"),
            FreeStringFunction = ReleaseName(plan, "free_string"),
            LicenseFiles = input.LicenseFiles.ToList(),
        };

        if (options.Packaging != null)
        {
            model.IsPackable = true;
            model.Packaging = Packaging(options.Packaging, model);
        }

        AddRuntimes(model, input);
        var types = new ManagedTypes(model.NativeClassName) { RootNamespace = model.RootNamespace };
        foreach (var (className, name) in MemberNameCandidates(plan, libClass).Where(c => typeNames.Contains(c.Name)))
        {
            if (!types.QualifiedTypeNamesByClass.TryGetValue(className, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                types.QualifiedTypeNamesByClass[className] = set;
            }

            set.Add(name);
        }

        var emitter = new FunctionEmitter(types, model.ExceptionClassName);
        var declarations = new List<List<string>>();
        var declared = new HashSet<string>(StringComparer.Ordinal);

        void Declare(string cName, List<string> lines)
        {
            if (declared.Add(cName))
            {
                declarations.Add(lines);
                model.Exports.Add(cName);
            }
        }

        var libSection = new ApiSectionModel { Title = "static class " + libClass, Summary = model.IsPassThrough ? "The crate's C exports, one static method each." : "The crate's free functions." };
        var libScope = new NameScope(CSharpNames.InheritedMemberNames.Append(libClass));
        var freeLines = new List<string>();

        if (!model.IsPassThrough)
        {
            foreach (var constant in plan.Constants)
            {
                var name = libScope.Claim(CSharpNames.Sanitize(constant.ManagedName));
                var lines = new CodeLines(1);
                lines.Doc(XmlDocText.Lines(constant.Doc, "The value of the Rust constant " + constant.RustPath + "."));
                lines.Doc(new[] { "<remarks>Rust: <c>" + XmlDocText.Escape(constant.RustPath + ": " + constant.RustType + " = " + constant.RustValue) + "</c>.</remarks>" });
                lines.Line("public const " + constant.ManagedType + " " + CSharpNames.Escape(name) + " = " + constant.ManagedLiteral + ";");
                AppendMember(freeLines, lines.Lines);
                libSection.Entries.Add(new ApiEntryModel { Signature = "const " + constant.ManagedType + " " + libClass + "." + name + " = " + constant.ManagedLiteral, Summary = XmlDocText.Summary(constant.Doc, "The Rust constant " + constant.RustPath + "."), RustPath = constant.RustPath });
            }

            AddEnums(model, plan);
            AddMirrorShells(model, plan, types, out var mirrorScopes, out var mirrorSections);
            AddHandles(model, plan, types, emitter, Declare, out var handleSections, out var placed);

            foreach (var function in plan.Functions)
            {
                if (placed.Contains(function) || function.Kind is FunctionKind.Destroy or FunctionKind.Release)
                {
                    continue;
                }

                var mirror = MirrorFor(function, plan);
                if (mirror != null)
                {
                    var file = model.Mirrors.First(m => m.Name == CSharpNames.Escape(mirror.ManagedName));
                    var isInstance = function.Parameters.Any(p => p.RustName == "self" && p.Role == ParameterRole.Value && p.Type?.Kind == MappingKind.MirrorStruct);
                    var placement = isInstance ? MemberPlacement.MirrorInstance : MemberPlacement.MirrorStatic;
                    var name = ClaimName(mirrorScopes[mirror], function, isInstance && IsToStringCandidate(function), mirror.ManagedName);
                    var member = emitter.Emit(function, placement, name, file.Name, mirror, null);
                    file.BodyLines.Add(string.Empty);
                    file.BodyLines.AddRange(member.Lines);
                    mirrorSections[mirror].Entries.Add(new ApiEntryModel { Signature = member.ApiSignature, Summary = member.Summary, RustPath = function.RustPath });
                    Declare(member.CName, member.NativeDeclaration);
                    continue;
                }

                var baseName = MemberName(function);
                var enumOwner = plan.Enums.FirstOrDefault(e => (!string.IsNullOrEmpty(function.OwnerRustPath) && (e.RustPath == function.OwnerRustPath || e.RustTypeName == function.OwnerRustPath)) || e.RustTypeName == CSharpNames.OwnerSegment(function.RustPath));
                if (enumOwner != null && !baseName.StartsWith(enumOwner.ManagedName, StringComparison.Ordinal))
                {
                    baseName = enumOwner.ManagedName + baseName;
                }

                var freeName = libScope.Claim(baseName);
                var free = emitter.Emit(function, MemberPlacement.FreeFunction, freeName, libClass, null, null);
                AppendMember(freeLines, free.Lines);
                libSection.Entries.Add(new ApiEntryModel { Signature = free.ApiSignature, Summary = free.Summary, RustPath = function.RustPath });
                Declare(free.CName, free.NativeDeclaration);
            }

            foreach (var mirror in plan.Mirrors)
            {
                model.ApiSections.Add(mirrorSections[mirror]);
            }

            model.ApiSections.AddRange(handleSections);
            foreach (var enumMapping in plan.Enums)
            {
                model.ApiSections.Add(new ApiSectionModel
                {
                    Title = "enum " + CSharpNames.Escape(enumMapping.ManagedName) + " : int",
                    Summary = XmlDocText.Summary(enumMapping.Doc, "Mirrors the Rust enum " + enumMapping.RustTypeName + "."),
                    Entries = enumMapping.Variants.Select(v => new ApiEntryModel { Signature = CSharpNames.Escape(v.ManagedName) + " = " + v.Value.ToString(CultureInfo.InvariantCulture), Summary = XmlDocText.Summary(v.Doc, string.Empty) }).ToList(),
                });
            }

            Declare(model.LastErrorFunction, FunctionEmitter.Declaration(model.LastErrorFunction, "int", new[] { "byte* buffer", "nuint capacity", "nuint* written" }));
            foreach (var release in new[] { model.FreeBufferFunction, model.FreeStringFunction }.Where(n => n != null))
            {
                Declare(release, FunctionEmitter.Declaration(release, "void", new[] { "Buffer buffer" }));
            }

            if (types.UsesTakeArray && model.FreeBufferFunction == null)
            {
                throw new WrapperGenerationException("The plan returns owned vectors but lists no " + prefix + "free_buffer release function (Kind Release).");
            }

            if (types.UsesTakeString && model.FreeStringFunction == null)
            {
                throw new WrapperGenerationException("The plan returns owned strings but lists no " + prefix + "free_string release function (Kind Release).");
            }
            model.UsesStrRef = types.UsesStrRef;
            AddNativeStructs(model, plan, types);
            AddArrays(model, types);
            model.ApiSections.Add(new ApiSectionModel
            {
                Title = "class " + model.ExceptionClassName + " : Exception",
                Summary = "Thrown when a native call fails: the Rust function returned an error (the message is its text), an argument was rejected, or the crate panicked.",
                Entries = new List<ApiEntryModel> { new ApiEntryModel { Signature = "int Status", Summary = "The native status code: -1 null argument, -2 invalid argument or length, -3 error returned by the crate, -4 panic inside the crate, -5 object already consumed." } },
            });
        }
        else
        {
            foreach (var function in plan.Functions)
            {
                if (function.Kind is FunctionKind.Destroy or FunctionKind.Release)
                {
                    continue;
                }

                var name = libScope.Claim(MemberName(function));
                var member = emitter.Emit(function, MemberPlacement.PassThrough, name, libClass, null, null);
                AppendMember(freeLines, member.Lines);
                libSection.Entries.Add(new ApiEntryModel { Signature = member.ApiSignature, Summary = member.Summary, RustPath = function.RustPath ?? function.CName });
                Declare(member.CName, member.NativeDeclaration);
            }
        }

        model.FreeFunctionLines = freeLines;
        model.ApiSections.Insert(0, libSection);
        // The first example calls a method: constants come first in the section but are not
        // callable, so the first entry that is not a constant is shown.
        var firstEntry = libSection.Entries.FirstOrDefault(e => !IsConstantEntry(e.Signature));
        if (firstEntry != null)
        {
            model.FirstEntrySignature = firstEntry.Signature;
            model.FirstEntryMethodName = EntryMethodName(firstEntry.Signature);
        }
        foreach (var declaration in declarations)
        {
            if (model.NativeDeclarationLines.Count > 0)
            {
                model.NativeDeclarationLines.Add(string.Empty);
            }

            model.NativeDeclarationLines.AddRange(declaration);
        }

        foreach (var entry in plan.Overrides.Where(o => !string.IsNullOrWhiteSpace(o.CSharpSource)))
        {
            var path = entry.RustPath ?? "Override";
            if (path.StartsWith("crate::", StringComparison.Ordinal))
            {
                path = path.Substring("crate::".Length);
            }

            var stem = CSharpNames.Pascal(string.Join("_", path.Split("::")));
            var fileName = stem + ".cs";
            for (var n = 2; model.Overrides.Any(o => o.FileName == fileName); n++)
            {
                fileName = stem + n.ToString(CultureInfo.InvariantCulture) + ".cs";
            }

            model.Overrides.Add(new OverrideModel { RustPath = entry.RustPath, FileName = fileName, Source = entry.CSharpSource.Replace("\r\n", "\n") });
        }

        model.Skipped = plan.Skipped.Select(s => new SkippedModel { RustPath = s.RustPath, Reason = s.Reason }).ToList();
        model.DocNotes = plan.DocNotes.Select(n => (string.IsNullOrEmpty(n.RustPath) ? string.Empty : n.RustPath + ": ") + n.Text).ToList();
        model.Warnings = plan.Warnings.ToList();
        foreach (var section in model.ApiSections)
        {
            section.Summary = string.IsNullOrWhiteSpace(section.Summary) ? null : section.Summary;
            foreach (var entry in section.Entries)
            {
                entry.Summary = string.IsNullOrWhiteSpace(entry.Summary) ? null : entry.Summary;
                entry.Signature = DocSignature(entry.Signature, model.RootNamespace);
            }
        }

        model.FirstEntrySignature = DocSignature(model.FirstEntrySignature, model.RootNamespace);

        return model;
    }

    private static string ReleaseName(MappingPlan plan, string suffix)
        => plan.Functions.FirstOrDefault(f => f.Kind == FunctionKind.Release && f.CName != null && f.CName.EndsWith(suffix, StringComparison.Ordinal))?.CName;

    private static PackagingModel Packaging(WrapperPackaging packaging, WrapperModel model)
    {
        static string Xml(string value) => string.IsNullOrWhiteSpace(value) ? null : SecurityElement.Escape(value.Trim());
        var tags = (packaging.Tags ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        return new PackagingModel
        {
            PackageId = Xml(packaging.PackageId),
            LicenseExpression = Xml(packaging.LicenseExpression),
            Authors = Xml(packaging.Authors),
            Description = Xml(packaging.Description) ?? Xml(".NET wrapper around the Rust crate " + model.CrateName + " " + model.CrateVersion + ": the native library for each included runtime identifier and an idiomatic C# API over it, generated by CodeBrix.RustTools.CrateBuilder."),
            ProjectUrl = Xml(packaging.ProjectUrl),
            RepositoryUrl = Xml(packaging.RepositoryUrl),
            Copyright = Xml(packaging.Copyright),
            Tags = tags.Count == 0 ? null : Xml(string.Join(";", tags)),
        };
    }

    private static void AddRuntimes(WrapperModel model, WrapperInput input)
    {
        var compiled = input.CompiledTargets.Where(t => t != null && t.Outcome == CrateTargetOutcome.Compiled && t.ArtifactPaths.Count > 0).OrderBy(t => t.Rid ?? CrateTargets.Rid(t.Target), StringComparer.Ordinal).ToList();
        foreach (var target in compiled)
        {
            var rid = target.Rid ?? CrateTargets.Rid(target.Target);
            var expected = CrateTargets.NativeFileName(target.Target, model.LibName);
            var files = target.ArtifactPaths.Select(System.IO.Path.GetFileName).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
            model.Runtimes.Add(new RuntimeModel
            {
                Rid = rid,
                FileName = files.Contains(expected, StringComparer.Ordinal) ? expected : files[0],
                Files = files,
                Note = CrateTargets.IsMacOS(target.Target) && target.Provenance == ArtifactProvenance.DevelopmentBuild ? "development and testing only: built without Apple's toolchain" : null,
            });
        }

        var present = new HashSet<string>(model.Runtimes.Select(r => r.Rid), StringComparer.Ordinal);
        foreach (var target in input.RequestedTargets.OrderBy(CrateTargets.Rid, StringComparer.Ordinal))
        {
            var rid = CrateTargets.Rid(target);
            if (!present.Contains(rid))
            {
                var result = input.CompiledTargets.FirstOrDefault(t => t != null && t.Target == target);
                model.MissingTargets.Add(new MissingTargetModel { Rid = rid, Reason = result?.Message ?? "it was not built (see the build record)" });
            }
        }

        model.RuntimeIdentifiersText = model.Runtimes.Count == 0 ? "none" : string.Join(", ", model.Runtimes.Select(r => r.Rid));
        model.RuntimeIdentifiersLiteral = string.Join(", ", model.Runtimes.Select(r => "\"" + r.Rid + "\""));
        foreach (var runtime in model.Runtimes)
        {
            model.FileNameArmLines.Add("        \"" + runtime.Rid + "\" => \"" + runtime.FileName + "\",");
        }
    }

    private static void AddEnums(WrapperModel model, MappingPlan plan)
    {
        foreach (var enumMapping in plan.Enums)
        {
            var file = new TypeFileModel { Name = CSharpNames.Escape(enumMapping.ManagedName), UnderlyingType = "int" };
            file.DocLines = Prefixed(0, XmlDocText.Lines(enumMapping.Doc, "Mirrors the Rust enum <c>" + XmlDocText.Escape(enumMapping.RustTypeName) + "</c>."));
            var lines = new CodeLines(1);
            var first = true;
            foreach (var variant in enumMapping.Variants)
            {
                if (!first)
                {
                    lines.Line(string.Empty);
                }

                first = false;
                lines.Doc(XmlDocText.Lines(variant.Doc, "The Rust variant <c>" + XmlDocText.Escape(variant.RustName) + "</c>."));
                lines.Line(CSharpNames.Escape(variant.ManagedName) + " = " + variant.Value.ToString(CultureInfo.InvariantCulture) + ",");
            }

            file.BodyLines = lines.Lines;
            model.Enums.Add(file);
        }
    }

    private static void AddMirrorShells(WrapperModel model, MappingPlan plan, ManagedTypes types, out Dictionary<MirrorStruct, NameScope> scopes, out Dictionary<MirrorStruct, ApiSectionModel> sections)
    {
        scopes = new Dictionary<MirrorStruct, NameScope>();
        sections = new Dictionary<MirrorStruct, ApiSectionModel>();
        foreach (var mirror in plan.Mirrors)
        {
            types.CurrentClass = mirror.ManagedName;
            var name = CSharpNames.Escape(mirror.ManagedName);
            var scope = new NameScope(CSharpNames.InheritedMemberNames.Concat(new[] { mirror.ManagedName, "ToNative", "FromNative" }));
            var nativeName = model.NativeClassName + "." + ManagedTypes.NativeStructName(mirror);
            var file = new TypeFileModel { Name = name, NativeName = nativeName };
            file.DocLines = Prefixed(0, XmlDocText.Lines(mirror.Doc, "Mirrors the Rust struct <c>" + XmlDocText.Escape(mirror.RustTypeName) + "</c> (copied by value)."));
            var section = new ApiSectionModel { Title = "readonly record struct " + name, Summary = XmlDocText.Summary(mirror.Doc, "Mirrors the Rust struct " + mirror.RustTypeName + ".") };

            var properties = new List<(string Name, string Parameter, MirrorField Field)>();
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < mirror.Fields.Count; i++)
            {
                var field = mirror.Fields[i];
                var raw = !string.IsNullOrWhiteSpace(field.ManagedName) ? field.ManagedName : CSharpNames.Pascal(string.IsNullOrEmpty(field.Name) ? "item" + i : (char.IsDigit(field.Name[0]) ? "item" + field.Name : field.Name));
                var propertyName = scope.Claim(CSharpNames.Sanitize(raw));
                var parameter = CSharpNames.Camel(propertyName);
                while (!parameterNames.Add(parameter))
                {
                    parameter += "_";
                }

                properties.Add((propertyName, parameter, field));
            }

            var lines = new CodeLines(1);
            if (properties.Count > 0)
            {
                lines.Doc(new[] { "<summary>Creates a <see cref=\"" + name + "\"/> from its fields.</summary>" });
                lines.Line("public " + name + "(" + string.Join(", ", properties.Select(p => types.Managed(p.Field.Type) + " " + p.Parameter)) + ")");
                lines.Open();
                foreach (var property in properties)
                {
                    lines.Line(property.Name + " = " + property.Parameter + ";");
                }

                lines.Close();
                section.Entries.Add(new ApiEntryModel { Signature = name + "(" + string.Join(", ", properties.Select(p => types.Managed(p.Field.Type) + " " + p.Parameter)) + ")", Summary = "Creates the value from its fields." });
            }

            foreach (var property in properties)
            {
                lines.Line(string.Empty);
                lines.Doc(XmlDocText.Lines(property.Field.Doc, "The Rust field <c>" + XmlDocText.Escape(property.Field.Name) + "</c>."));
                lines.Line("public " + types.Managed(property.Field.Type) + " " + property.Name + " { get; init; }");
                section.Entries.Add(new ApiEntryModel { Signature = types.Managed(property.Field.Type) + " " + property.Name + " { get; init; }", Summary = XmlDocText.Summary(property.Field.Doc, string.Empty) });
            }

            lines.Line(string.Empty);
            lines.Doc(new[] { "<summary>The blittable form passed to the native library.</summary>" });
            lines.Line("internal " + nativeName + " ToNative() => new " + nativeName);
            lines.Open();
            foreach (var property in properties)
            {
                lines.Line(NativeFieldName(property.Field, mirror.Fields.IndexOf(property.Field)) + " = " + types.ToInterop(property.Name, property.Field.Type) + ",");
            }

            lines.Close(";");
            lines.Line(string.Empty);
            lines.Doc(new[] { "<summary>Creates the value from its blittable form.</summary>" });
            lines.Line("internal static " + name + " FromNative(" + nativeName + " value) => new " + name + "(" + string.Join(", ", properties.Select(p => types.FromInterop("value." + NativeFieldName(p.Field, mirror.Fields.IndexOf(p.Field)), p.Field.Type))) + ");");
            file.BodyLines = lines.Lines;
            model.Mirrors.Add(file);
            scopes[mirror] = scope;
            sections[mirror] = section;
        }
    }

    private static void AddHandles(WrapperModel model, MappingPlan plan, ManagedTypes types, FunctionEmitter emitter, Action<string, List<string>> declare, out List<ApiSectionModel> sections, out HashSet<ExportedFunction> placed)
    {
        sections = new List<ApiSectionModel>();
        placed = new HashSet<ExportedFunction>();
        foreach (var handle in plan.Handles)
        {
            var name = CSharpNames.Escape(handle.ManagedName);
            var scope = new NameScope(CSharpNames.InheritedMemberNames.Concat(HandleReserved).Append(handle.ManagedName));
            var destroy = handle.Destroy?.CName ?? (handle.CName ?? model.LibName + "_" + handle.ManagedName.ToLowerInvariant()) + "_destroy";
            var file = new TypeFileModel { Name = name, SafeHandleName = handle.ManagedName + "SafeHandle", DestroyFunction = CSharpNames.Escape(CSharpNames.Sanitize(destroy)) };
            file.DocLines = Prefixed(0, XmlDocText.Lines(handle.Doc, "Wraps the Rust type <c>" + XmlDocText.Escape(handle.RustTypeName) + "</c>."));
            file.DocLines.Add("/// <remarks>");
            file.DocLines.Add("/// <para>Owns a native object: call <see cref=\"Dispose\"/> (or use <c>using</c>) when done; the");
            file.DocLines.Add("/// finalizer of the inner safe handle releases it otherwise.</para>");
            file.DocLines.Add("/// <para>The native object is not thread-safe: calls on one instance are serialised by a lock.</para>");
            file.DocLines.Add("/// </remarks>");
            var section = new ApiSectionModel { Title = "sealed class " + name + " : IDisposable", Summary = XmlDocText.Summary(handle.Doc, "Wraps the Rust type " + handle.RustTypeName + ".") };
            var functions = plan.Functions.Where(f => OwnerOf(f, plan) == handle).Concat(handle.Methods.Where(m => m != null && !plan.Functions.Contains(m))).ToList();
            var lines = new List<string>();
            var constructorUsed = false;
            foreach (var function in functions)
            {
                placed.Add(function);
                if (function.Kind is FunctionKind.Destroy or FunctionKind.Release)
                {
                    continue;
                }

                var hasSelf = function.Parameters.Any(p => p.Role == ParameterRole.Handle && (p.RustName == "self" || p.Type?.Handle == handle || p.Type?.Handle == null));
                var isConstructor = function.Kind == FunctionKind.Constructor && !hasSelf;
                if (isConstructor)
                {
                    var optional = function.Return?.Shape == ReturnShape.OptionalHandle;
                    var managed = MemberName(function);
                    if (!optional && !constructorUsed && managed == "New")
                    {
                        constructorUsed = true;
                        var helperName = scope.Claim("NewNative");
                        var helper = emitter.Emit(function, MemberPlacement.HandleConstructorHelper, helperName, name, null, handle);
                        var ctor = new CodeLines(1);
                        ctor.Doc(XmlDocText.Lines(function.Doc, "Creates the native object."));
                        ctor.Doc(new[] { "<exception cref=\"" + model.ExceptionClassName + "\">The native call failed.</exception>" });
                        ctor.Line("public " + name + "(" + helper.ParameterList + ")");
                        ctor.Line("    : this(FromNativeHandle(" + helperName + "(" + helper.ArgumentList + ")))");
                        ctor.Open();
                        ctor.Close();
                        AppendMember(lines, ctor.Lines);
                        AppendMember(lines, helper.Lines);
                        section.Entries.Add(new ApiEntryModel { Signature = name + "(" + helper.ParameterList + ")", Summary = helper.Summary, RustPath = function.RustPath });
                        declare(helper.CName, helper.NativeDeclaration);
                        if (helper.IsParameterless)
                        {
                            model.ParameterlessConstructions.Add("new " + name + "()");
                        }

                        continue;
                    }

                    var factoryName = scope.Claim(managed);
                    var factory = emitter.Emit(function, MemberPlacement.HandleFactory, factoryName, name, null, handle);
                    AppendMember(lines, factory.Lines);
                    section.Entries.Add(new ApiEntryModel { Signature = factory.ApiSignature, Summary = factory.Summary, RustPath = function.RustPath });
                    declare(factory.CName, factory.NativeDeclaration);
                    if (factory.IsParameterless && !optional && function.Return?.Shape is ReturnShape.Handle)
                    {
                        model.ParameterlessConstructions.Add(name + "." + factoryName + "()");
                    }

                    continue;
                }

                if (!hasSelf)
                {
                    var staticName = scope.Claim(StripOwner(MemberName(function), handle.ManagedName));
                    var staticMember = emitter.Emit(function, MemberPlacement.HandleFactory, staticName, name, null, handle);
                    AppendMember(lines, staticMember.Lines);
                    section.Entries.Add(new ApiEntryModel { Signature = staticMember.ApiSignature, Summary = staticMember.Summary, RustPath = function.RustPath });
                    declare(staticMember.CName, staticMember.NativeDeclaration);
                    continue;
                }

                if (function.Kind == FunctionKind.ConsumingMethod)
                {
                    file.HasConsumingMethod = true;
                }

                var methodName = ClaimName(scope, function, IsToStringCandidate(function));
                var method = emitter.Emit(function, MemberPlacement.HandleMethod, methodName, name, null, handle);
                AppendMember(lines, method.Lines);
                section.Entries.Add(new ApiEntryModel { Signature = method.ApiSignature, Summary = method.Summary, RustPath = function.RustPath });
                declare(method.CName, method.NativeDeclaration);
            }

            section.Entries.Add(new ApiEntryModel { Signature = "void Dispose()", Summary = "Releases the native object (idempotent)." });
            declare(destroy, FunctionEmitter.DestroyDeclaration(destroy));
            file.BodyLines = lines.Count == 0 ? lines : new List<string> { string.Empty }.Concat(lines).ToList();
            model.Handles.Add(file);
            model.HandleNames.Add(name);
            sections.Add(section);
        }
    }

    private static HandleType OwnerOf(ExportedFunction function, MappingPlan plan)
    {
        if (function.Owner != null)
        {
            return function.Owner;
        }

        if (!string.IsNullOrEmpty(function.OwnerRustPath))
        {
            var owned = plan.Handles.FirstOrDefault(h => h.RustPath == function.OwnerRustPath || h.RustTypeName == function.OwnerRustPath);
            if (owned != null)
            {
                return owned;
            }
        }

        var self = function.Parameters.FirstOrDefault(p => p.Role == ParameterRole.Handle && p.RustName == "self");
        return self?.Type?.Handle;
    }

    private static MirrorStruct MirrorFor(ExportedFunction function, MappingPlan plan)
    {
        if (!string.IsNullOrEmpty(function.OwnerRustPath))
        {
            return plan.Mirrors.FirstOrDefault(m => m.RustPath == function.OwnerRustPath || m.RustTypeName == function.OwnerRustPath);
        }

        var owner = CSharpNames.OwnerSegment(function.RustPath);
        if (owner == null)
        {
            return null;
        }

        var self = function.Parameters.FirstOrDefault(p => p.RustName == "self" && p.Role == ParameterRole.Value && p.Type?.Kind == MappingKind.MirrorStruct);
        if (self?.Type?.Mirror != null && plan.Mirrors.Contains(self.Type.Mirror))
        {
            return self.Type.Mirror;
        }

        return plan.Mirrors.FirstOrDefault(m => m.RustTypeName == owner);
    }

    private static void AddNativeStructs(WrapperModel model, MappingPlan plan, ManagedTypes types)
    {
        types.CurrentClass = model.NativeClassName;
        foreach (var mirror in plan.Mirrors)
        {
            var lines = new CodeLines(1);
            if (model.NativeStructLines.Count > 0)
            {
                lines.Line(string.Empty);
            }

            // With the plan's layout numbers (the facade asserts the same numbers at Rust
            // compile time) the struct is pinned byte for byte: explicit size and offsets.
            var pinned = HasPlannedLayout(mirror);
            lines.Line("/// <summary>The C layout of <c>" + XmlDocText.Escape(mirror.RustTypeName) + "</c> (" + XmlDocText.Escape(mirror.CName) + ")"
                + (pinned ? ", " + mirror.ByteSize.ToString(CultureInfo.InvariantCulture) + " bytes" : string.Empty) + ".</summary>");
            lines.Line(pinned
                ? "[StructLayout(LayoutKind.Explicit, Size = " + mirror.ByteSize.ToString(CultureInfo.InvariantCulture) + ")]"
                : "[StructLayout(LayoutKind.Sequential)]");
            lines.Line("internal struct " + ManagedTypes.NativeStructName(mirror));
            lines.Open();
            for (var i = 0; i < mirror.Fields.Count; i++)
            {
                var field = mirror.Fields[i];
                if (pinned)
                {
                    lines.Line("[FieldOffset(" + field.Offset.ToString(CultureInfo.InvariantCulture) + ")]");
                }

                lines.Line("public " + types.Interop(field.Type) + " " + NativeFieldName(field, i) + ";");
            }

            lines.Close();
            model.NativeStructLines.AddRange(lines.Lines);
        }
    }

    /// <summary>Whether the plan carries the mirror's C layout (size and every field offset;
    /// the planner fills them for every mirror it makes).</summary>
    private static bool HasPlannedLayout(MirrorStruct mirror)
        => mirror.ByteSize > 0
           && mirror.Fields.Count > 0
           && mirror.Fields.All(f => f.Offset >= 0 && f.Offset < mirror.ByteSize)
           && mirror.Fields.Skip(1).All(f => f.Offset > 0);

    private static void AddArrays(WrapperModel model, ManagedTypes types)
    {
        foreach (var spec in types.Arrays)
        {
            var file = new TypeFileModel { Name = spec.Name, Length = spec.Length, ElementType = spec.ElementType };
            file.DocLines.Add("/// <summary>A fixed-size array of " + spec.Length.ToString(CultureInfo.InvariantCulture) + " <see cref=\"" + spec.ElementType + "\"/> values, the C# form of the Rust type <c>" + XmlDocText.Escape(spec.RustType) + "</c> (index it like an array; <c>Span</c> conversions work).</summary>");
            model.Arrays.Add(file);
        }
    }

    private static string NativeFieldName(MirrorField field, int index)
    {
        var raw = !string.IsNullOrWhiteSpace(field.CName) ? field.CName : field.Name;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "item" + index.ToString(CultureInfo.InvariantCulture);
        }

        return CSharpNames.Escape(CSharpNames.Sanitize(raw));
    }

    /// <summary>"Vec2Dot" on Vec2 -&gt; "Dot" (the planner prefixes type-owned free functions
    /// with the type name; on the type itself the prefix is redundant).</summary>
    private static string StripOwner(string name, string ownerPrefix)
    {
        if (string.IsNullOrEmpty(ownerPrefix) || name.Length <= ownerPrefix.Length || !name.StartsWith(ownerPrefix, StringComparison.Ordinal))
        {
            return name;
        }

        var rest = name.Substring(ownerPrefix.Length);
        return char.IsUpper(rest[0]) ? rest : name;
    }

    /// <summary>Every (class, name) a generated method, property or constant may get (before
    /// de-duplication; an over-approximation): inside that class a type of the same name is
    /// written fully qualified, because a member hides a type of the same spelling.</summary>
    private static IEnumerable<(string Class, string Name)> MemberNameCandidates(MappingPlan plan, string libClass)
    {
        foreach (var function in plan.Functions)
        {
            var name = MemberName(function);
            var classes = new List<string>();
            if (function.Owner != null)
            {
                classes.Add(function.Owner.ManagedName);
            }
            else
            {
                classes.Add(libClass);
                var mirror = MirrorFor(function, plan);
                if (mirror != null)
                {
                    classes.Add(mirror.ManagedName);
                }

                var owner = OwnerOf(function, plan);
                if (owner != null)
                {
                    classes.Add(owner.ManagedName);
                }
            }

            var names = new List<string> { name, "Get" + name };
            foreach (var owner in plan.Handles.Select(h => h.ManagedName).Concat(plan.Mirrors.Select(m => m.ManagedName)).Concat(plan.Enums.Select(e => e.ManagedName)))
            {
                var stripped = StripOwner(name, owner);
                if (stripped != name)
                {
                    names.Add(stripped);
                    names.Add("Get" + stripped);
                }
            }

            foreach (var c in classes)
            {
                foreach (var n in names)
                {
                    yield return (c, n);
                }
            }
        }

        foreach (var constant in plan.Constants)
        {
            yield return (libClass, CSharpNames.Sanitize(constant.ManagedName));
        }

        foreach (var mirror in plan.Mirrors)
        {
            for (var i = 0; i < mirror.Fields.Count; i++)
            {
                // a property whose type has its own name is fine (C#'s "Color Color" rule); one
                // of another type hides the type of its name
                var field = mirror.Fields[i];
                var name = !string.IsNullOrWhiteSpace(field.ManagedName) ? field.ManagedName : CSharpNames.Pascal(string.IsNullOrEmpty(field.Name) ? "item" + i : (char.IsDigit(field.Name[0]) ? "item" + field.Name : field.Name));
                var typeName = field.Type?.Mirror?.ManagedName ?? field.Type?.Enum?.ManagedName ?? field.Type?.Handle?.ManagedName ?? field.Type?.ManagedType;
                if (name != typeName)
                {
                    yield return (mirror.ManagedName, name);
                }
            }
        }
    }

    private static string MemberName(ExportedFunction function)
    {
        var raw = !string.IsNullOrWhiteSpace(function.ManagedName) ? function.ManagedName : CSharpNames.Pascal(CSharpNames.LastSegment(function.RustPath ?? function.CName));
        return CSharpNames.Sanitize(raw);
    }

    private static string ClaimName(NameScope scope, ExportedFunction function, bool allowToString, string ownerPrefix = null)
    {
        var raw = StripOwner(MemberName(function), ownerPrefix);
        if (allowToString && raw == "ToString" && !scope.IsUsed("ToString!"))
        {
            scope.Reserve("ToString!");
            return "ToString";
        }

        return scope.Claim(raw);
    }

    private static bool IsToStringCandidate(ExportedFunction function)
        => function.Return?.Shape is ReturnShape.OwnedString or ReturnShape.ResultString
            && function.Parameters.All(p => p.Role is ParameterRole.Handle or ParameterRole.OutBuffer or ParameterRole.OutLength || p.RustName == "self");

    private static void AppendMember(List<string> lines, List<string> member)
    {
        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.AddRange(member);
    }

    private static List<string> Prefixed(int indent, IReadOnlyList<string> docLines)
        => docLines.Select(l => new string(' ', indent * 4) + "/// " + l).ToList();

    /// <summary>The method name in an API signature such as
    /// <c>static string Datacrate.Greet(string name)</c> (<c>Greet</c>), or <c>null</c>.</summary>
    /// <summary>True for the API-reference line of a constant (<c>const T Class.Name = value</c>),
    /// which is not a method a first example can call.</summary>
    internal static bool IsConstantEntry(string signature)
        => signature != null && signature.StartsWith("const ", StringComparison.Ordinal);

    /// <summary>The signature as the README files show it: the <c>global::&lt;RootNamespace&gt;.</c>
    /// qualification that the generated code needs where a member shares a type's name is noise in
    /// documentation (every reader has <c>using &lt;RootNamespace&gt;;</c>), so it is removed.</summary>
    internal static string DocSignature(string signature, string rootNamespace)
        => string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(rootNamespace)
            ? signature
            : signature.Replace("global::" + rootNamespace + ".", string.Empty, StringComparison.Ordinal);

    internal static string EntryMethodName(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        // The parameter list is the parenthesised group that ends the signature (a tuple return
        // type has parentheses of its own).
        var trimmed = signature.TrimEnd();
        var open = -1;
        if (trimmed.EndsWith(')'))
        {
            var depth = 0;
            for (var i = trimmed.Length - 1; i >= 0; i--)
            {
                depth += trimmed[i] == ')' ? 1 : trimmed[i] == '(' ? -1 : 0;
                if (depth == 0)
                {
                    open = i;
                    break;
                }
            }
        }

        signature = trimmed;
        var head = (open < 0 ? signature : signature.Substring(0, open)).TrimEnd();
        var space = head.LastIndexOf(' ');
        var name = space < 0 ? head : head.Substring(space + 1);
        var dot = name.LastIndexOf('.');
        name = dot < 0 ? name : name.Substring(dot + 1);
        var generic = name.IndexOf('<');
        return generic < 0 ? name : name.Substring(0, generic);
    }
}
