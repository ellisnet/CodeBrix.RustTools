using System;
using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// What <see cref="WrapperModelBuilder"/> needs, gathered by the writer.
/// </summary>
internal sealed class WrapperInput
{
    /// <summary>The mapping plan.</summary>
    public MappingPlan Plan { get; set; }

    /// <summary>The wrapper settings.</summary>
    public WrapperOptions Options { get; set; }

    /// <summary>The requested targets.</summary>
    public IReadOnlyList<CrateTarget> RequestedTargets { get; set; } = Array.Empty<CrateTarget>();

    /// <summary>The compiled targets.</summary>
    public IReadOnlyList<CrateTargetResult> CompiledTargets { get; set; } = Array.Empty<CrateTargetResult>();

    /// <summary>Whether the request links the CodeBrix WebGPU engine.</summary>
    public bool LinkToCodeBrixWebGpu { get; set; }

    /// <summary>The CodeBrix.WebGpu.Native version to reference.</summary>
    public string WebGpuPackageVersion { get; set; }

    /// <summary>The Rust package name.</summary>
    public string CrateName { get; set; }

    /// <summary>The Rust package version.</summary>
    public string CrateVersion { get; set; }

    /// <summary>The crate's licence expression, or <c>null</c>.</summary>
    public string CrateLicense { get; set; }

    /// <summary>One line about where the crate came from.</summary>
    public string SourceDescription { get; set; }

    /// <summary>The licence files that will be copied (file names under licenses/).</summary>
    public IReadOnlyList<string> LicenseFiles { get; set; } = Array.Empty<string>();
}
