namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// How retrieved artifacts are arranged under
/// <see cref="ArtifactRetrievalOptions.HostDirectory"/>.
/// </summary>
public enum ArtifactLayout
{
    /// <summary>Every file directly in the host folder (<c>&lt;host&gt;/libmy_lib.so</c>). Meant
    /// for one target: two targets would write files with the same names.</summary>
    Flat,

    /// <summary>One sub-folder per Rust target triple
    /// (<c>&lt;host&gt;/x86_64-pc-windows-gnullvm/my_lib.dll</c>).</summary>
    ByTarget,

    /// <summary>The NuGet native-asset layout, one folder per .NET runtime identifier
    /// (<c>&lt;host&gt;/runtimes/win-x64/native/my_lib.dll</c>,
    /// <c>&lt;host&gt;/runtimes/android-arm64/native/libmy_lib.so</c>), ready to be packed into
    /// a NuGet package.</summary>
    NuGetRuntimes,
}
