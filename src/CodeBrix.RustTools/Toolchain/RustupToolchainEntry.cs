namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// One line of <c>rustup toolchain list</c>.
/// </summary>
internal sealed record RustupToolchainEntry(string Name, bool IsDefault, bool IsActive);
