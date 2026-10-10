namespace CodeBrix.RustTools.Building;

/// <summary>
/// The cargo subcommands that <see cref="CargoRunner"/> can run.
/// </summary>
public enum CargoCommand
{
    /// <summary><c>cargo build</c> - compile the package and its dependencies.</summary>
    Build,

    /// <summary><c>cargo check</c> - type-check without producing executables (fast).</summary>
    Check,

    /// <summary><c>cargo test</c> - build and run the unit, integration and doc tests.</summary>
    Test,

    /// <summary><c>cargo clean</c> - remove the <c>target</c> folder (or part of it).</summary>
    Clean,

    /// <summary><c>cargo doc</c> - build the API documentation.</summary>
    Doc,

    /// <summary><c>cargo clippy</c> - run the clippy lints (needs the clippy component).</summary>
    Clippy,

    /// <summary><c>cargo fmt</c> - check (default) or apply rustfmt formatting (needs the
    /// rustfmt component).</summary>
    Fmt,
}
