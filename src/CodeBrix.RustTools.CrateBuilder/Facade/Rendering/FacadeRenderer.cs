using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// Renders the C facade crate of a <see cref="MappingPlan"/> into
/// <c>&lt;workspace.Directory&gt;/.codebrix-facade/&lt;FacadeCrateName&gt;/</c>: Cargo.toml (a
/// cdylib named after the upstream library, path dependency on the untouched upstream crate,
/// empty [workspace], panic = "unwind"), the upstream Cargo.lock when there is one, src/lib.rs
/// (status codes, last-error message, buffers, object boxes, mirrors, enums and one
/// <c>extern "C"</c> function per planned export, each behind a panic boundary), the C header
/// <c>&lt;LibName&gt;.h</c>, build.rs when the plan links the CodeBrix WebGPU engine, the
/// consumer's additional facade files, FACADE-README.txt and generated-files.json. Rendering the
/// same plan twice gives identical bytes; a re-render removes the files the previous render
/// generated and this one does not, and nothing else. In pass-through mode
/// (<see cref="MappingPlan.UpstreamIsCdylib"/>) nothing is written.
/// </summary>
internal sealed class FacadeRenderer
{
    /// <summary>Renders the facade (see the class remarks). Throws
    /// <see cref="FacadeGenerationException"/> (stage RenderingFacade) for a plan the facade
    /// cannot express, a template failure or a file that cannot be written.</summary>
    public FacadeRenderResult Render(MappingPlan plan, StagedWorkspace workspace, FacadeRenderOptions o)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);
        o ??= new FacadeRenderOptions();

        if (plan.UpstreamIsCdylib)
        {
            return new FacadeRenderResult
            {
                Generated = false,
                LibName = plan.LibName,
                FacadeCrateName = plan.FacadeCrateName,
                Exports = plan.Functions.Select(f => f.CName).ToList(),
            };
        }

        if (string.IsNullOrWhiteSpace(workspace.Directory))
        {
            throw new FacadeGenerationException("The staged workspace has no folder to write the facade into.");
        }

        var upstreamManifest = workspace.UpstreamManifestPath ?? workspace.ManifestPath
            ?? throw new FacadeGenerationException("The staged workspace names no upstream Cargo.toml.");
        var directory = FacadeDirectory(workspace.Directory, plan.FacadeCrateName);
        var packageName = FirstNonBlank(o.UpstreamPackageName, workspace.Identity?.CrateName, plan.CrateName)
            ?? throw new FacadeGenerationException("The upstream package name is not known (no option, identity or plan crate name).");
        var version = FirstNonBlank(o.UpstreamVersion, workspace.Identity?.CrateVersion) ?? "0.0.0";
        if (!version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+'))
        {
            throw new FacadeGenerationException($"The upstream version '{version}' is not a package version.");
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var extraModules = CopyAdditionalFiles(plan, files);
        var builder = new FacadeSourceBuilder(plan);
        var model = builder.Build(packageName, version, UpstreamDependency(directory, upstreamManifest, packageName, o), extraModules);
        model.WorkspacePatches = WorkspacePatches(directory, workspace.WorkspaceManifestPath ?? upstreamManifest);
        model.WorkspaceProfiles = WorkspaceProfiles(workspace.WorkspaceManifestPath ?? upstreamManifest);

        files["Cargo.toml"] = GeneratedFilePublisher.Utf8(FacadeTemplates.Render(FacadeTemplates.CargoToml, model));
        files["src/lib.rs"] = GeneratedFilePublisher.Utf8(FacadeTemplates.Render(FacadeTemplates.LibRs, model));
        files[model.HeaderFileName] = GeneratedFilePublisher.Utf8(FacadeTemplates.Render(FacadeTemplates.Header, model));
        files[FacadeConventions.ReadmeFileName] = GeneratedFilePublisher.Utf8(FacadeTemplates.Render(FacadeTemplates.Readme, model));
        if (plan.LinkToCodeBrixWebGpu)
        {
            files["build.rs"] = GeneratedFilePublisher.Utf8(FacadeTemplates.Render(FacadeTemplates.BuildRs, model));
        }

        var lockFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspace.WorkspaceManifestPath ?? upstreamManifest)), "Cargo.lock");
        if (File.Exists(lockFile))
        {
            files["Cargo.lock"] = File.ReadAllBytes(lockFile);
        }

        var outcome = GeneratedFilePublisher.Publish(directory, files);
        return new FacadeRenderResult
        {
            Generated = true,
            Directory = directory,
            ManifestPath = Path.Combine(directory, "Cargo.toml"),
            HeaderPath = Path.Combine(directory, model.HeaderFileName),
            LibName = plan.LibName,
            FacadeCrateName = plan.FacadeCrateName,
            GeneratedFiles = files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            ChangedFiles = outcome.Changed.Where(c => c != FacadeConventions.GeneratedFilesManifestName).ToList(),
            RemovedFiles = outcome.Removed,
            Exports = model.Exports.ToList(),
        };
    }

    /// <summary>The facade crate folder for a staged workspace folder.</summary>
    public static string FacadeDirectory(string workspaceDirectory, string facadeCrateName)
        => Path.Combine(Path.GetFullPath(workspaceDirectory), FacadeConventions.FacadeFolderName, facadeCrateName);

    /// <summary>
    /// The <c>[patch.&lt;source&gt;]</c> and <c>[replace]</c> tables of the staged upstream
    /// workspace root manifest, as TOML text for the facade's Cargo.toml, with every relative
    /// <c>path</c> re-based from the workspace root onto the facade folder. cargo applies
    /// patches only from the ROOT manifest of the workspace being built - the facade is its
    /// own workspace - so without them the upstream crate would be built against different
    /// dependencies than its own workspace uses. Empty when there are none or the manifest
    /// cannot be read.
    /// </summary>
    public static string WorkspacePatches(string facadeDirectory, string workspaceManifestPath)
    {
        if (string.IsNullOrEmpty(workspaceManifestPath) || !File.Exists(workspaceManifestPath))
        {
            return string.Empty;
        }

        TomlTable document;
        try
        {
            document = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(workspaceManifestPath), new TomlSerializerOptions { SourceName = workspaceManifestPath });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Empty;
        }

        var root = Path.GetDirectoryName(Path.GetFullPath(workspaceManifestPath));
        var sb = new StringBuilder();
        void Section(string header, TomlTable table)
        {
            if (table == null || table.Count == 0)
            {
                return;
            }

            sb.Append('\n').Append(header).Append('\n');
            foreach (var entry in table)
            {
                sb.Append(TomlKey(entry.Key)).Append(" = ").Append(TomlValue(entry.Value, root, facadeDirectory, "")).Append('\n');
            }
        }

        if (document != null && document.TryGetValue("patch", out var patchValue) && patchValue is TomlTable patches)
        {
            foreach (var source in patches)
            {
                Section("[patch." + TomlKey(source.Key) + "]", source.Value as TomlTable);
            }
        }

        if (document != null && document.TryGetValue("replace", out var replaceValue) && replaceValue is TomlTable replace)
        {
            Section("[replace]", replace);
        }

        return sb.Length == 0
            ? string.Empty
            : "\n# The upstream workspace's patches (cargo reads them only from the root manifest of the\n# workspace being built), with their paths re-based on this folder." + sb;
    }

    /// <summary>
    /// The <c>[profile.*]</c> tables for the facade's Cargo.toml: the upstream workspace root
    /// manifest's profiles (cargo reads profiles only from the ROOT manifest of the workspace
    /// being built - the facade is its own workspace - so without them the upstream crate would
    /// be compiled with cargo's default settings instead of its workspace's, e.g. without its
    /// <c>lto</c> / <c>codegen-units</c> / <c>opt-level</c> choices), with <c>panic</c> always
    /// <c>"unwind"</c> in <c>dev</c> and <c>release</c> (the export boundary catches panics with
    /// <c>catch_unwind</c>) and every other <c>panic</c> key left out. Always holds
    /// <c>[profile.dev]</c> and <c>[profile.release]</c>; nested tables (<c>package.*</c>,
    /// <c>build-override</c>) and custom profiles are copied as they are.
    /// </summary>
    public static string WorkspaceProfiles(string workspaceManifestPath)
    {
        TomlTable profiles = null;
        if (!string.IsNullOrEmpty(workspaceManifestPath) && File.Exists(workspaceManifestPath))
        {
            try
            {
                var document = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(workspaceManifestPath), new TomlSerializerOptions { SourceName = workspaceManifestPath });
                if (document != null && document.TryGetValue("profile", out var value))
                {
                    profiles = value as TomlTable;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                profiles = null;
            }
        }

        var sb = new StringBuilder("# The export boundary catches panics (catch_unwind), which needs unwinding.");
        var copied = profiles != null && profiles.Count > 0;
        if (copied)
        {
            sb.Append("\n# The other settings are the upstream workspace's profiles (cargo reads them only from the\n# root manifest of the workspace being built).");
        }

        var names = new List<string> { "dev", "release" };
        if (copied)
        {
            names.AddRange(profiles.Keys.Where(k => k is not "dev" and not "release"));
        }

        foreach (var name in names)
        {
            var table = copied && profiles.TryGetValue(name, out var value) ? value as TomlTable : null;
            var abort = table != null && table.TryGetValue("panic", out var panic) && panic is string mode && mode != "unwind";
            var header = "[profile." + TomlKey(name) + "]";
            sb.Append("\n\n").Append(header);
            if (abort)
            {
                sb.Append("\n# The upstream workspace sets panic = \"").Append(table["panic"]).Append("\" here; the facade needs \"unwind\".");
            }

            if (name is "dev" or "release")
            {
                sb.Append("\npanic = \"unwind\"");
            }

            if (table != null)
            {
                ProfileTable(sb, header, table, true);
            }
        }

        return sb.ToString();
    }

    private static void ProfileTable(StringBuilder sb, string header, TomlTable table, bool topLevel)
    {
        foreach (var entry in table.Where(e => e.Value is not TomlTable))
        {
            if (topLevel && entry.Key == "panic")
            {
                continue;
            }

            sb.Append('\n').Append(TomlKey(entry.Key)).Append(" = ").Append(TomlValue(entry.Value, string.Empty, string.Empty, string.Empty));
        }

        foreach (var entry in table.Where(e => e.Value is TomlTable))
        {
            var child = header.Substring(0, header.Length - 1) + "." + TomlKey(entry.Key) + "]";
            var childTable = (TomlTable)entry.Value;
            if (childTable.Any(e => e.Value is not TomlTable))
            {
                // A table that only holds tables ([profile.dev.package]) needs no header of its own.
                sb.Append("\n\n").Append(child);
            }

            ProfileTable(sb, child, childTable, false);
        }
    }

    private static string TomlKey(string key)
        => !string.IsNullOrEmpty(key) && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? key : RustSyntax.TomlString(key);

    private static string TomlValue(object value, string root, string facadeDirectory, string key)
    {
        switch (value)
        {
            case string text when key == "path" && !Path.IsPathRooted(text):
                return RustSyntax.TomlString(Path.GetRelativePath(facadeDirectory, Path.GetFullPath(Path.Combine(root, text))).Replace('\\', '/'));
            case string text:
                return RustSyntax.TomlString(text);
            case bool flag:
                return flag ? "true" : "false";
            case long or int or double:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            case TomlTable table:
                return "{ " + string.Join(", ", table.Select(e => TomlKey(e.Key) + " = " + TomlValue(e.Value, root, facadeDirectory, e.Key))) + " }";
            case TomlArray array:
                return "[" + string.Join(", ", array.Select(item => TomlValue(item, root, facadeDirectory, string.Empty))) + "]";
            default:
                return RustSyntax.TomlString(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The TOML inline table of the upstream dependency (relative '/' path from the facade
    /// folder to the upstream crate folder, package name, default features, features).</summary>
    public static string UpstreamDependency(string facadeDirectory, string upstreamManifestPath, string packageName, FacadeRenderOptions o)
    {
        var crateDirectory = Path.GetDirectoryName(Path.GetFullPath(upstreamManifestPath));
        var relative = Path.GetRelativePath(facadeDirectory, crateDirectory).Replace('\\', '/');
        var features = o.EffectiveFeatures();
        return "{ package = " + RustSyntax.TomlString(packageName)
            + ", path = " + RustSyntax.TomlString(relative)
            + ", default-features = " + (o.NoDefaultFeatures ? "false" : "true")
            + ", features = [" + string.Join(", ", features.Select(RustSyntax.TomlString)) + "] }";
    }

    private static List<string> CopyAdditionalFiles(MappingPlan plan, Dictionary<string, byte[]> files)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "lib", "main", "facade_support", "overrides", "wgpu_ffi", "up", "upstream" };
        var modules = new List<string>();
        foreach (var source in plan.AdditionalFacadeFiles)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                throw new FacadeGenerationException($"The additional facade file '{source}' does not exist.");
            }

            var name = Path.GetFileNameWithoutExtension(source);
            if (!string.Equals(Path.GetExtension(source), ".rs", StringComparison.Ordinal) || !RustSyntax.IsIdentifier(name) || RustSyntax.Ident(name) != name)
            {
                throw new FacadeGenerationException($"The additional facade file '{source}' must be a .rs file whose name is a Rust module name.");
            }

            if (!reserved.Add(name))
            {
                throw new FacadeGenerationException($"The additional facade file '{source}' uses the module name '{name}', which is taken.");
            }

            files["src/" + name + ".rs"] = File.ReadAllBytes(source);
            modules.Add(name);
        }

        return modules;
    }

    private static string FirstNonBlank(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
