namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Where the crate to build comes from: a <see cref="LocalManifestSource"/> (a Cargo.toml on
/// this machine) or a <see cref="GitRepositorySource"/> (a repository to clone). The crate's
/// own folder is never written to: CrateBuilder builds a staged copy.
/// </summary>
public abstract class CrateSource
{
    private protected CrateSource()
    {
    }

    /// <summary>
    /// Checks the source's own settings; called by <see cref="CrateBuildRequest.Validate"/>.
    /// </summary>
    /// <exception cref="System.ArgumentException">A setting is missing or invalid.</exception>
    internal abstract void Validate();

    /// <summary>A copy of the source (the credentials callback is shared, not copied).</summary>
    internal abstract CrateSource Clone();
}
