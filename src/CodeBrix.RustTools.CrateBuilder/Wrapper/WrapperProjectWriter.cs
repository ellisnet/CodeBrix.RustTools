using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Model;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Rendering;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper;

/// <summary>
/// Stage <see cref="CrateBuildStage.Wrapping"/>: writes the .NET 10 wrapper class library
/// around the compiled targets, rendered with the embedded Scriban templates from the
/// mapping plan (<see cref="FacadeGenerationResult.Plan"/>).
/// </summary>
/// <remarks>
/// <para>The folder (<see cref="WrapperOptions.OutputDirectory"/>, else a folder named after the
/// assembly beside the request's output folder) receives the csproj, Native/ (the
/// <c>LibraryImport</c> declarations, the blittable structs and the loader), one file per
/// handle class / mirror / enum / inline array, the library class with the free functions,
/// the exception type, the native files under runtimes/&lt;rid&gt;/native/, README.md,
/// AGENT-README.txt, THIRD-PARTY-NOTICES.txt with the crate's licence files in licenses/,
/// WRAP-REPORT.txt (and .json), Overrides/ for hand-written C#, and Tests/ with a smoke-test
/// file when asked for.</para>
/// <para>generated-files.json lists every file written; a re-write removes the listed files it
/// no longer produces and touches nothing else. A non-empty folder without that manifest is
/// refused. Files whose bytes are unchanged are not rewritten. Output is deterministic.</para>
/// </remarks>
internal sealed class WrapperProjectWriter : IWrapperWriter
{
    /// <summary>The folder of the copied licence files.</summary>
    public const string LicensesFolder = "licenses";

    /// <summary>The wrap report's file name in the wrapper folder.</summary>
    public const string WrapReportFileName = "WRAP-REPORT.txt";

    /// <summary>The wrap report's JSON twin in the wrapper folder.</summary>
    public const string WrapReportJsonFileName = "WRAP-REPORT.json";

    private static readonly string[] LicenseFilePrefixes = { "LICENSE", "LICENCE", "COPYING", "NOTICE", "UNLICENSE", "COPYRIGHT" };
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    /// <inheritdoc />
    public Task<WrapperResult> WriteAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, FacadeGenerationResult facade, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Write(workspace, compiledTargets, facade, request, context, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            throw new WrapperGenerationException("The wrapper project could not be written: " + ex.Message, ex);
        }
    }

    /// <summary>The folder the wrapper goes to: <see cref="WrapperOptions.OutputDirectory"/>,
    /// else <c>&lt;request.OutputDirectory&gt;/../&lt;AssemblyName&gt;</c>.</summary>
    public static string ResolveOutputDirectory(CrateBuildRequest request)
    {
        var options = request.Wrapper;
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            return Path.GetFullPath(options.OutputDirectory);
        }

        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.OutputDirectory));
        var parent = Path.GetDirectoryName(output) ?? output;
        return Path.Combine(parent, options.AssemblyName.Trim());
    }

    private static WrapperResult Write(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, FacadeGenerationResult facade, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        if (request?.Wrapper == null)
        {
            throw new WrapperGenerationException("The request has no wrapper settings (CrateBuildRequest.Wrapper).");
        }

        request.Wrapper.Validate();
        if (string.IsNullOrWhiteSpace(request.OutputDirectory) && string.IsNullOrWhiteSpace(request.Wrapper.OutputDirectory))
        {
            throw new WrapperGenerationException("Neither CrateBuildRequest.OutputDirectory nor WrapperOptions.OutputDirectory is set; the wrapper has no folder.");
        }

        var plan = facade?.Plan ?? throw new WrapperGenerationException("No mapping plan reached the wrapper writer (FacadeGenerationResult.Plan is null): the wrapper's C# is generated from it.");
        var directory = ResolveOutputDirectory(request);
        context?.Report(CrateBuildStage.Wrapping, "Rendering the wrapper project " + request.Wrapper.AssemblyName + " in " + directory + ".");

        var licenses = FindLicenseFiles(workspace);
        var facts = ReadCrateFacts(workspace);
        var input = new WrapperInput
        {
            Plan = plan,
            Options = request.Wrapper,
            RequestedTargets = (request.Targets ?? new List<CrateTarget>()).ToList(),
            CompiledTargets = (compiledTargets ?? Array.Empty<CrateTargetResult>()).ToList(),
            LinkToCodeBrixWebGpu = request.LinkToCodeBrixWebGpu,
            WebGpuPackageVersion = context?.WebGpuNativeVersion ?? BuildRecordFactory.ReferencedWebGpuNativeVersion,
            CrateName = workspace?.Identity?.CrateName ?? facts.Name ?? plan.CrateName,
            CrateVersion = workspace?.Identity?.CrateVersion ?? facts.Version,
            CrateLicense = facts.License,
            SourceDescription = DescribeSource(workspace?.Identity),
            LicenseFiles = licenses.Keys.ToList(),
        };
        var model = WrapperModelBuilder.Build(input);
        var files = Render(model, cancellationToken);

        foreach (var runtime in model.Runtimes)
        {
            var target = input.CompiledTargets.First(t => (t.Rid ?? CrateTargets.Rid(t.Target)) == runtime.Rid);
            foreach (var path in target.ArtifactPaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!File.Exists(path))
                {
                    throw new WrapperGenerationException($"The native file {path} of {runtime.Rid} does not exist; it cannot be copied into the wrapper.");
                }

                files["runtimes/" + runtime.Rid + "/native/" + Path.GetFileName(path)] = FileContent.Copy(path);
            }
        }

        foreach (var (name, path) in licenses)
        {
            files[LicensesFolder + "/" + name] = FileContent.Copy(path);
        }

        var reportSource = facade.WrapReportPath;
        if (!string.IsNullOrWhiteSpace(reportSource) && File.Exists(reportSource))
        {
            files[WrapReportFileName] = FileContent.Copy(reportSource);
            var json = new[] { reportSource + ".json", Path.ChangeExtension(reportSource, ".json") }.FirstOrDefault(File.Exists);
            if (json != null)
            {
                files[WrapReportJsonFileName] = FileContent.Copy(json);
            }
        }
        else
        {
            files[WrapReportFileName] = FileContent.Text(WrapperTemplates.Render(WrapperTemplates.WrapReport, model, null, cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var written = WriteFiles(directory, files);
        var projectPath = Path.Combine(directory, model.AssemblyName + ".csproj");
        context?.Report(CrateBuildStage.Wrapping, "Wrote " + written.Count + " files of " + model.AssemblyName + " (" + model.RuntimeIdentifiersText + ").");
        return new WrapperResult
        {
            ProjectPath = projectPath,
            ProjectDirectory = directory,
            EntryClassName = model.LibClassName,
            WrapReportPath = Path.Combine(directory, WrapReportFileName),
            GeneratedFiles = written,
        };
    }

    /// <summary>Renders every template file of the wrapper (relative path -&gt; text).</summary>
    internal static SortedDictionary<string, FileContent> Render(WrapperModel model, CancellationToken cancellationToken)
    {
        var files = new SortedDictionary<string, FileContent>(StringComparer.Ordinal);

        void Add(string path, string template, object item = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files[path] = FileContent.Text(WrapperTemplates.Render(template, model, item, cancellationToken));
        }

        Add(model.AssemblyName + ".csproj", WrapperTemplates.Project);
        Add("Native/AssemblyInfo.cs", WrapperTemplates.AssemblyInfo);
        Add("Native/" + model.NativeClassName + ".cs", WrapperTemplates.Native);
        Add(model.LibClassName + ".cs", WrapperTemplates.Library);
        if (!model.IsPassThrough)
        {
            Add(model.ExceptionClassName + ".cs", WrapperTemplates.Exception);
        }

        foreach (var mirror in model.Mirrors)
        {
            Add("Mirrors/" + FileStem(mirror.Name) + ".cs", WrapperTemplates.Mirror, mirror);
        }

        foreach (var array in model.Arrays)
        {
            Add("Arrays/" + FileStem(array.Name) + ".cs", WrapperTemplates.InlineArray, array);
        }

        foreach (var enumFile in model.Enums)
        {
            Add("Enums/" + FileStem(enumFile.Name) + ".cs", WrapperTemplates.Enum, enumFile);
        }

        foreach (var handle in model.Handles)
        {
            Add(FileStem(handle.Name) + ".cs", WrapperTemplates.Handle, handle);
        }

        foreach (var entry in model.Overrides)
        {
            files["Overrides/" + entry.FileName] = FileContent.Text(entry.Source.EndsWith('\n') ? entry.Source : entry.Source + "\n");
        }

        Add("README.md", WrapperTemplates.Readme);
        Add("AGENT-README.txt", WrapperTemplates.AgentReadme);
        Add("THIRD-PARTY-NOTICES.txt", WrapperTemplates.Notices);
        if (model.HasSmokeTests)
        {
            Add("Tests/" + model.LibClassName + "SmokeTests.cs", WrapperTemplates.SmokeTests);
        }

        return files;
    }

    private static string FileStem(string typeName) => typeName.TrimStart('@');

    private static List<string> WriteFiles(string directory, SortedDictionary<string, FileContent> files)
    {
        var previous = GeneratedFilesManifest.Load(directory);
        if (Directory.Exists(directory) && previous == null && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new WrapperGenerationException($"The wrapper folder {directory} is not empty and was not written by CrateBuilder (it has no {GeneratedFilesManifest.FileName}); choose an empty or new folder (WrapperOptions.OutputDirectory).");
        }

        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(directory);
        var written = new List<string>();
        foreach (var (relative, content) in files)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new WrapperGenerationException($"The generated file name '{relative}' leaves the wrapper folder.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var bytes = content.Read();
            if (!File.Exists(path) || !SameBytes(path, bytes))
            {
                File.WriteAllBytes(path, bytes);
            }

            written.Add(path);
        }

        if (previous != null)
        {
            foreach (var stale in previous.Files.Where(f => !files.ContainsKey(f)))
            {
                var path = Path.GetFullPath(Path.Combine(root, stale));
                if (path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
                {
                    File.Delete(path);
                    RemoveEmptyParents(Path.GetDirectoryName(path), root);
                }
            }
        }

        var manifestPath = Path.Combine(root, GeneratedFilesManifest.FileName);
        var manifest = GeneratedFilesManifest.For(files.Keys).ToBytes();
        if (!File.Exists(manifestPath) || !SameBytes(manifestPath, manifest))
        {
            File.WriteAllBytes(manifestPath, manifest);
        }

        written.Add(manifestPath);
        written.Sort(StringComparer.Ordinal);
        return written;
    }

    private static void RemoveEmptyParents(string folder, string root)
    {
        var stop = Path.TrimEndingDirectorySeparator(root);
        while (folder != null && folder.Length > stop.Length && folder.StartsWith(stop, StringComparison.Ordinal) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
            folder = Path.GetDirectoryName(folder);
        }
    }

    private static bool SameBytes(string path, byte[] bytes)
    {
        var info = new FileInfo(path);
        return info.Length == bytes.LongLength && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
    }

    private static string DescribeSource(CrateSourceIdentity identity)
    {
        if (identity == null)
        {
            return "a Cargo.toml";
        }

        if (identity.Kind == CrateSourceIdentity.GitKind && !string.IsNullOrWhiteSpace(identity.Url))
        {
            var commit = string.IsNullOrWhiteSpace(identity.Commit) ? string.Empty : " at commit " + identity.Commit;
            var manifest = string.IsNullOrWhiteSpace(identity.ManifestPath) ? string.Empty : ", manifest " + identity.ManifestPath.Replace('\\', '/');
            return "the Git repository " + identity.Url + commit + manifest;
        }

        return "a local Cargo.toml";
    }

    private sealed class CrateFacts
    {
        public string Name { get; set; }

        public string Version { get; set; }

        public string License { get; set; }

        public string LicenseFile { get; set; }
    }

    private static CrateFacts ReadCrateFacts(StagedWorkspace workspace)
    {
        var manifestPath = workspace?.UpstreamManifestPath ?? workspace?.ManifestPath;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return new CrateFacts();
        }

        var manifest = WorkspaceRootFinder.LoadManifest(manifestPath);
        var root = !string.IsNullOrWhiteSpace(workspace.WorkspaceManifestPath) && File.Exists(workspace.WorkspaceManifestPath) && !StagingFileSystem.PathEquals(workspace.WorkspaceManifestPath, manifestPath)
            ? WorkspaceRootFinder.LoadManifest(workspace.WorkspaceManifestPath)
            : manifest;
        var package = manifest.Package?.WithInheritance(root.Workspace?.Package);
        return new CrateFacts
        {
            Name = package?.Name,
            Version = package?.Version,
            License = string.IsNullOrWhiteSpace(package?.License) ? (string.IsNullOrWhiteSpace(package?.LicenseFile) ? null : "see licenses/" + Path.GetFileName(package.LicenseFile)) : package.License,
            LicenseFile = package?.LicenseFile,
        };
    }

    /// <summary>The licence files of the upstream crate folder and of its workspace root
    /// (file name in licenses/ -&gt; source path), plus the manifest's <c>license-file</c>.</summary>
    internal static SortedDictionary<string, string> FindLicenseFiles(StagedWorkspace workspace)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var manifestPath = workspace?.UpstreamManifestPath ?? workspace?.ManifestPath;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return result;
        }

        var crateDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        var folders = new List<(string Folder, string Prefix)> { (crateDirectory, string.Empty) };
        if (!string.IsNullOrWhiteSpace(workspace.WorkspaceManifestPath))
        {
            var rootDirectory = Path.GetDirectoryName(Path.GetFullPath(workspace.WorkspaceManifestPath));
            if (!StagingFileSystem.PathEquals(rootDirectory, crateDirectory))
            {
                folders.Add((rootDirectory, "workspace-"));
            }
        }

        foreach (var (folder, prefix) in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (!LicenseFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AddLicense(result, name, file, prefix);
            }
        }

        var facts = ReadCrateFacts(workspace);
        if (!string.IsNullOrWhiteSpace(facts.LicenseFile))
        {
            var path = Path.GetFullPath(Path.Combine(crateDirectory, facts.LicenseFile));
            if (File.Exists(path) && !result.Values.Any(v => StagingFileSystem.PathEquals(v, path)))
            {
                AddLicense(result, Path.GetFileName(path), path, string.Empty);
            }
        }

        return result;
    }

    private static void AddLicense(SortedDictionary<string, string> result, string name, string path, string prefix)
    {
        if (!result.TryGetValue(name, out var existing))
        {
            result[name] = path;
            return;
        }

        if (File.ReadAllBytes(existing).AsSpan().SequenceEqual(File.ReadAllBytes(path)))
        {
            return;
        }

        var alternative = (prefix.Length == 0 ? "crate-" : prefix) + name;
        for (var n = 2; result.ContainsKey(alternative); n++)
        {
            alternative = (prefix.Length == 0 ? "crate-" : prefix) + n + "-" + name;
        }

        result[alternative] = path;
    }

    /// <summary>One file's content: rendered text or a file to copy.</summary>
    internal sealed class FileContent
    {
        private readonly string _text;
        private readonly string _source;

        private FileContent(string text, string source)
        {
            _text = text;
            _source = source;
        }

        /// <summary>The source file of a copy, or <c>null</c>.</summary>
        public string SourcePath => _source;

        /// <summary>Rendered text (UTF-8, no BOM, <c>\n</c>).</summary>
        public static FileContent Text(string text) => new FileContent(text.Replace("\r\n", "\n"), null);

        /// <summary>A copy of a file.</summary>
        public static FileContent Copy(string path) => new FileContent(null, path);

        /// <summary>The bytes to write.</summary>
        public byte[] Read() => _source != null ? File.ReadAllBytes(_source) : Utf8.GetBytes(_text);

        /// <summary>The text, or <c>null</c> for a copy.</summary>
        public string AsText() => _text;
    }
}
