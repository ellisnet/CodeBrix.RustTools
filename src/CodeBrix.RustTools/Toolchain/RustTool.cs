namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The tools that make up a working Rust build environment.
/// </summary>
public enum RustTool
{
    /// <summary>The rustup toolchain manager (optional; distro-packaged Rust works without it).</summary>
    Rustup,

    /// <summary>The cargo build tool and package manager.</summary>
    Cargo,

    /// <summary>The rustc compiler.</summary>
    Rustc,

    /// <summary>The clippy linter (<c>cargo clippy</c>), a rustup component.</summary>
    Clippy,

    /// <summary>The rustfmt formatter (<c>cargo fmt</c>), a rustup component.</summary>
    Rustfmt,

    /// <summary>The rustdoc documentation generator (<c>cargo doc</c>).</summary>
    Rustdoc,

    /// <summary>The C compiler (cc on Linux and macOS, MSVC cl on Windows) used by build scripts
    /// and by crates that compile C code.</summary>
    CCompiler,

    /// <summary>The system linker rustc uses to produce executables (the cc driver on Linux and
    /// macOS, MSVC link on Windows).</summary>
    Linker,
}
