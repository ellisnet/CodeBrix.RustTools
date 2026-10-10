namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// What a retrieved file is.
/// </summary>
public enum ArtifactKind
{
    /// <summary>A program (a <c>bin</c> or <c>example</c> target): no extension on Linux and
    /// Android, <c>.exe</c> on Windows.</summary>
    Executable,

    /// <summary>A shared library from a <c>cdylib</c> or <c>dylib</c> target: <c>.so</c> on
    /// Linux and Android, <c>.dll</c> on Windows.</summary>
    SharedLibrary,

    /// <summary>A static library from a <c>staticlib</c> target (<c>.a</c>), or the
    /// <c>.dll.a</c> import library the Windows GNU linker writes beside a DLL.</summary>
    StaticLibrary,

    /// <summary>A Rust library (<c>.rlib</c>), usable only by other Rust code built with the
    /// same compiler.</summary>
    Rlib,

    /// <summary>Anything else.</summary>
    Other,
}
