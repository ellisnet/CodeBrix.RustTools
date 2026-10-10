namespace CodeBrix.RustTools.Projects;

/// <summary>
/// Conversions between <see cref="CargoTargetKind"/> and the strings cargo uses.
/// </summary>
public static class CargoTargetKinds
{
    /// <summary>
    /// Converts a cargo kind or crate-type string (<c>bin</c>, <c>lib</c>, <c>proc-macro</c>,
    /// <c>custom-build</c>, ...) to a <see cref="CargoTargetKind"/>; unrecognised text gives
    /// <see cref="CargoTargetKind.Unknown"/>.
    /// </summary>
    /// <param name="text">The kind text, case-insensitive.</param>
    public static CargoTargetKind Parse(string text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "bin" => CargoTargetKind.Bin,
        "lib" => CargoTargetKind.Lib,
        "rlib" => CargoTargetKind.Rlib,
        "dylib" => CargoTargetKind.Dylib,
        "cdylib" => CargoTargetKind.Cdylib,
        "staticlib" => CargoTargetKind.Staticlib,
        "proc-macro" => CargoTargetKind.ProcMacro,
        "example" => CargoTargetKind.Example,
        "test" => CargoTargetKind.Test,
        "bench" => CargoTargetKind.Bench,
        "custom-build" => CargoTargetKind.CustomBuild,
        _ => CargoTargetKind.Unknown,
    };

    /// <summary>
    /// Returns cargo's string for a kind (<c>proc-macro</c>, <c>custom-build</c>, ...).
    /// </summary>
    /// <param name="kind">The kind.</param>
    public static string ToCargoString(CargoTargetKind kind) => kind switch
    {
        CargoTargetKind.Bin => "bin",
        CargoTargetKind.Lib => "lib",
        CargoTargetKind.Rlib => "rlib",
        CargoTargetKind.Dylib => "dylib",
        CargoTargetKind.Cdylib => "cdylib",
        CargoTargetKind.Staticlib => "staticlib",
        CargoTargetKind.ProcMacro => "proc-macro",
        CargoTargetKind.Example => "example",
        CargoTargetKind.Test => "test",
        CargoTargetKind.Bench => "bench",
        CargoTargetKind.CustomBuild => "custom-build",
        _ => "unknown",
    };

    /// <summary>
    /// True for the kinds that are libraries (lib, rlib, dylib, cdylib, staticlib, proc-macro).
    /// </summary>
    /// <param name="kind">The kind.</param>
    public static bool IsLibrary(CargoTargetKind kind)
        => kind is CargoTargetKind.Lib or CargoTargetKind.Rlib or CargoTargetKind.Dylib
            or CargoTargetKind.Cdylib or CargoTargetKind.Staticlib or CargoTargetKind.ProcMacro;
}
