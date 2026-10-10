namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One way a resolved dependency edge is used (one entry of <c>dep_kinds</c>).
/// </summary>
public sealed class CargoResolveDependencyKind
{
    /// <summary>Normal, dev or build.</summary>
    public CargoDependencyKind Kind { get; init; }

    /// <summary>The platform condition, or <c>null</c> for every platform.</summary>
    public string Target { get; init; }

    /// <inheritdoc />
    public override string ToString() => Target == null ? Kind.ToString() : $"{Kind} ({Target})";
}
