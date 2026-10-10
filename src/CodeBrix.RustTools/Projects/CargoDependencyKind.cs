namespace CodeBrix.RustTools.Projects;

/// <summary>
/// Which dependency table a dependency comes from.
/// </summary>
public enum CargoDependencyKind
{
    /// <summary><c>[dependencies]</c>: needed by the crate itself.</summary>
    Normal,

    /// <summary><c>[dev-dependencies]</c>: needed only by tests, examples and benchmarks.</summary>
    Dev,

    /// <summary><c>[build-dependencies]</c>: needed only by the build script.</summary>
    Build,
}
