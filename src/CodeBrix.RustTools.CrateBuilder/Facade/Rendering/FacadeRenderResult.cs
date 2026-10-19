using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// What <see cref="FacadeRenderer.Render"/> wrote: the facade crate folder and its files, and
/// the C symbols the built library exports.
/// </summary>
internal sealed class FacadeRenderResult
{
    /// <summary>Whether a facade crate was written; <c>false</c> in pass-through mode (the
    /// upstream crate is already a cdylib with C exports and is built as it is).</summary>
    public bool Generated { get; set; }

    /// <summary>The facade crate folder
    /// (<c>&lt;workspace&gt;/.codebrix-facade/&lt;FacadeCrateName&gt;</c>); <c>null</c> when not
    /// <see cref="Generated"/>.</summary>
    public string Directory { get; set; }

    /// <summary>The facade crate's Cargo.toml; <c>null</c> when not <see cref="Generated"/>.</summary>
    public string ManifestPath { get; set; }

    /// <summary>The C header (<c>&lt;LibName&gt;.h</c>); <c>null</c> when not
    /// <see cref="Generated"/>.</summary>
    public string HeaderPath { get; set; }

    /// <summary>The library name: the built file is <c>lib&lt;LibName&gt;.so</c> /
    /// <c>lib&lt;LibName&gt;.dylib</c> / <c>&lt;LibName&gt;.dll</c>, and the wrapper loads
    /// "&lt;LibName&gt;".</summary>
    public string LibName { get; set; }

    /// <summary>The facade package name (<c>[package] name</c>).</summary>
    public string FacadeCrateName { get; set; }

    /// <summary>Every file this render generated, relative to <see cref="Directory"/>, with '/'
    /// separators, ordinal order (generated-files.json lists the same set; it does not list
    /// itself).</summary>
    public IReadOnlyList<string> GeneratedFiles { get; set; } = Array.Empty<string>();

    /// <summary>The files a re-render removed because the previous render generated them and this
    /// one did not (relative paths).</summary>
    public IReadOnlyList<string> RemovedFiles { get; set; } = Array.Empty<string>();

    /// <summary>The files whose bytes changed (or that are new) in this render (relative paths);
    /// an identical re-render leaves this empty.</summary>
    public IReadOnlyList<string> ChangedFiles { get; set; } = Array.Empty<string>();

    /// <summary>The exported C symbol names: <c>&lt;prefix&gt;last_error_message</c> first, then
    /// every planned function in plan order (in pass-through mode: the plan's functions). Symbols
    /// that override sources define are not listed.</summary>
    public IReadOnlyList<string> Exports { get; set; } = Array.Empty<string>();

    /// <summary>The pipeline's view of this result (<see cref="FacadeGenerationResult"/>).</summary>
    public FacadeGenerationResult ToGenerationResult() => new FacadeGenerationResult
    {
        Generated = Generated,
        FacadeDirectory = Directory,
        ManifestPath = ManifestPath,
        CrateName = Generated ? FacadeCrateName : null,
        ExportedFunctions = Exports.Select(e => new FacadeExport { Name = e }).ToList(),
    };
}
