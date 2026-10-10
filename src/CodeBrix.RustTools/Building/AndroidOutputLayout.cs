namespace CodeBrix.RustTools.Building;

/// <summary>
/// Where <see cref="RustWorkbench.BuildAndroidAsync"/> puts the files it built, below the output
/// folder.
/// </summary>
public enum AndroidOutputLayout
{
    /// <summary><c>jniLibs/&lt;abi&gt;/</c> (<c>arm64-v8a</c>, <c>x86_64</c>): the folder an
    /// Android app module packages into its APK.</summary>
    JniLibs,

    /// <summary><c>runtimes/&lt;rid&gt;/native/</c> (<c>android-arm64</c>, <c>android-x64</c>):
    /// the NuGet native-library layout.</summary>
    NuGetRuntimes,

    /// <summary><c>&lt;triple&gt;/</c> (<c>aarch64-linux-android</c>, <c>x86_64-linux-android</c>).</summary>
    ByTarget,
}
