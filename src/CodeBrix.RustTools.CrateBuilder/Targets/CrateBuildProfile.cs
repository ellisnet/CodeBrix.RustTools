namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The cargo profile a crate is built with.
/// </summary>
public enum CrateBuildProfile
{
    /// <summary><c>cargo build --release</c>: optimised binaries.</summary>
    Release,

    /// <summary><c>cargo build</c>: the dev profile, unoptimised with debug information.</summary>
    Debug,
}
