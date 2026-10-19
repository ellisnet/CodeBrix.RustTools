using System;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Extraction;

/// <summary>
/// Settings of <see cref="ApiExtractor"/>; the defaults are what the pipeline uses.
/// </summary>
internal sealed class ApiExtractorOptions
{
    /// <summary>In Docker mode, build and run the tool with the host's Rust toolchain when it
    /// is ready (cargo, rustc and a C toolchain). Default true; false forces the Docker
    /// route.</summary>
    public bool PreferHostToolchain { get; set; } = true;

    /// <summary>The Docker route when the host toolchain is not used. Default
    /// <see cref="ApiExtractorDockerRoute.Auto"/>.</summary>
    public ApiExtractorDockerRoute DockerRoute { get; set; } = ApiExtractorDockerRoute.Auto;

    /// <summary>The limit for one run of the tool. Default 10 minutes.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The limit for building the tool. Default 30 minutes.</summary>
    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
