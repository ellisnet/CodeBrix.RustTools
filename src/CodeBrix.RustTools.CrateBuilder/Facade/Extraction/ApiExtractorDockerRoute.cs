namespace CodeBrix.RustTools.CrateBuilder.Facade.Extraction;

/// <summary>
/// How <see cref="ApiExtractor"/> runs the tool in Docker mode when the host has no usable
/// Rust toolchain.
/// </summary>
internal enum ApiExtractorDockerRoute
{
    /// <summary><see cref="RetrieveAndRunOnHost"/> on a Linux host whose architecture matches
    /// the engine's, else <see cref="RunInContainer"/>.</summary>
    Auto,

    /// <summary>Build the tool in a container, copy the Linux executable to the host and run
    /// it there (Linux hosts of the engine's architecture only).</summary>
    RetrieveAndRunOnHost,

    /// <summary>Build the tool in a container, copy the staged workspace into the same
    /// container and run the tool there; the JSON comes back on standard output.</summary>
    RunInContainer,
}
