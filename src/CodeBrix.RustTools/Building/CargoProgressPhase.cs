namespace CodeBrix.RustTools.Building;

/// <summary>
/// The kind of event a <see cref="CargoProgress"/> report describes.
/// </summary>
public enum CargoProgressPhase
{
    /// <summary>Cargo printed a line that matches no other phase.</summary>
    Other,

    /// <summary><c>Updating crates.io index</c> (or another registry or git source).</summary>
    Updating,

    /// <summary><c>Locking N packages</c> - Cargo.lock is being written.</summary>
    Locking,

    /// <summary><c>Downloading</c> / <c>Downloaded</c> - crate sources are being fetched.</summary>
    Downloading,

    /// <summary><c>Compiling foo v1.2.3</c> - a crate started compiling.</summary>
    Compiling,

    /// <summary><c>Checking foo v1.2.3</c> - a crate started type-checking.</summary>
    Checking,

    /// <summary><c>Documenting foo v1.2.3</c> - rustdoc started on a crate.</summary>
    Documenting,

    /// <summary>A crate's first artifact (other than its build script) was produced, so the
    /// crate counts as done and <see cref="CargoProgress.Completed"/> went up by one; a crate with
    /// both a library and a binary counts once.</summary>
    CrateCompleted,

    /// <summary><c>Running</c> / <c>Doc-tests</c> - a test binary (or a verbose rustc command)
    /// started.</summary>
    Running,

    /// <summary><c>Finished</c> - the build part of the command finished.</summary>
    Finished,

    /// <summary>A <c>warning:</c> line from cargo or a build script, or a failed
    /// <see cref="Licensing.LicenseGateMode.ReportOnly"/> license audit ("License audit failed
    /// (report only): N rejected, M unresolved") reported before cargo starts.</summary>
    Warning,

    /// <summary>An <c>error:</c> line from cargo.</summary>
    Error,
}
