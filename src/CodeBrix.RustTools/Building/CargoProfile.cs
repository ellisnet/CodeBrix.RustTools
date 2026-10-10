namespace CodeBrix.RustTools.Building;

/// <summary>
/// The built-in cargo build profiles. A custom profile is chosen with
/// <see cref="CargoBuildOptions.CustomProfileName"/>.
/// </summary>
public enum CargoProfile
{
    /// <summary>The <c>dev</c> profile (no flag; output in <c>target/debug</c>).</summary>
    Debug,

    /// <summary>The <c>release</c> profile (<c>--release</c>; output in <c>target/release</c>).</summary>
    Release,
}
