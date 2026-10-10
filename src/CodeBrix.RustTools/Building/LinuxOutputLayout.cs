namespace CodeBrix.RustTools.Building;

/// <summary>
/// Where <see cref="RustWorkbench.BuildLinuxAsync"/> puts the files it built, below the output
/// folder.
/// </summary>
public enum LinuxOutputLayout
{
    /// <summary><c>runtimes/&lt;rid&gt;/native/</c> (<c>linux-arm64</c>, <c>linux-riscv64</c>,
    /// <c>linux-x64</c>): the NuGet native-library layout.</summary>
    NuGetRuntimes,

    /// <summary><c>&lt;triple&gt;/</c> (<c>aarch64-unknown-linux-gnu</c>,
    /// <c>riscv64gc-unknown-linux-gnu</c>).</summary>
    ByTriple,
}
