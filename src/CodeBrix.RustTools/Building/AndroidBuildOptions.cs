using System.Collections.Generic;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// What <see cref="RustWorkbench.BuildAndroidAsync"/> builds and how it lays the result out.
/// </summary>
public sealed class AndroidBuildOptions
{
    /// <summary>The link profile to apply; <c>null</c> (the default) makes one from the NDK the
    /// toolchain detection found, at <see cref="ApiLevel"/>.</summary>
    public AndroidLinkProfile Profile { get; set; }

    /// <summary>The API level to link for when <see cref="Profile"/> is <c>null</c>.</summary>
    public int ApiLevel { get; set; } = AndroidLinkProfile.DefaultApiLevel;

    /// <summary>The targets to build, in order; both 64-bit targets by default.</summary>
    public IList<string> Triples { get; } = new List<string>(AndroidLinkProfile.Triples);

    /// <summary>The folder layout of the output; <see cref="AndroidOutputLayout.JniLibs"/> by
    /// default.</summary>
    public AndroidOutputLayout Layout { get; set; } = AndroidOutputLayout.JniLibs;

    /// <summary>Whether programs (binaries) are copied as well as shared libraries. Default
    /// false: an APK carries libraries only.</summary>
    public bool IncludeExecutables { get; set; }
}
