namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Whether a build runs the facade stages (API extraction, planning, rendering) for its crate
/// (<see cref="CrateBuildRequest.FacadeMode"/>).
/// </summary>
public enum FacadeMode
{
    /// <summary>The default. The facade stages run when a wrapper is asked for or the crate is
    /// not a cdylib. A cdylib built without a wrapper is built as it is; when verification then
    /// finds that it exports nothing, the SAME build is routed through the facade stages
    /// automatically (extract, plan, render, build the facade), with a warning, and the build
    /// record's <see cref="FacadeRecord.Reason"/> says so.</summary>
    Auto,

    /// <summary>The facade stages never run: the crate is built as it is (it must be a cdylib
    /// with <c>extern "C"</c> exports). Cannot be combined with a wrapper, whose declarations
    /// come from the facade stages.</summary>
    Never,

    /// <summary>The facade stages always run, and a C facade is generated over the crate's
    /// Rust API even when the crate is a cdylib that already has C exports (they are then not
    /// passed through).</summary>
    Always,
}
