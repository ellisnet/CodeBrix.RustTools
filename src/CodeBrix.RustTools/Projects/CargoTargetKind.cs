namespace CodeBrix.RustTools.Projects;

/// <summary>
/// The kind of a cargo build target, as declared in Cargo.toml or reported by
/// <c>cargo metadata</c> (its <c>kind</c> and <c>crate_types</c> values).
/// </summary>
public enum CargoTargetKind
{
    /// <summary>A kind this library does not recognise.</summary>
    Unknown,

    /// <summary>An executable binary (<c>[[bin]]</c>, <c>src/main.rs</c>, <c>src/bin/*.rs</c>).</summary>
    Bin,

    /// <summary>A Rust library (<c>[lib]</c>, <c>src/lib.rs</c>).</summary>
    Lib,

    /// <summary>A Rust library compiled as an rlib.</summary>
    Rlib,

    /// <summary>A Rust dynamic library (dylib).</summary>
    Dylib,

    /// <summary>A C-compatible dynamic library (cdylib).</summary>
    Cdylib,

    /// <summary>A C-compatible static library (staticlib).</summary>
    Staticlib,

    /// <summary>A procedural-macro library.</summary>
    ProcMacro,

    /// <summary>An example program (<c>[[example]]</c>, <c>examples/*.rs</c>).</summary>
    Example,

    /// <summary>An integration test (<c>[[test]]</c>, <c>tests/*.rs</c>).</summary>
    Test,

    /// <summary>A benchmark (<c>[[bench]]</c>, <c>benches/*.rs</c>).</summary>
    Bench,

    /// <summary>A build script (<c>build.rs</c>; cargo names it <c>build-script-build</c>).</summary>
    CustomBuild,
}
