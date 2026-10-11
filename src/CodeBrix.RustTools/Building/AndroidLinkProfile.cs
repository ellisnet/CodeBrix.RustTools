using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The settings that build an Android target with the Android NDK's LLVM toolchain: for a
/// target triple and an API level, the <c>clang</c> wrapper becomes cargo's linker
/// (<c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c>) and the <c>cc</c> crate's compiler
/// (<c>CC_&lt;triple&gt;</c>, <c>CXX_&lt;triple&gt;</c>), and <c>llvm-ar</c> the archiver
/// (<c>AR_&lt;triple&gt;</c>, <c>RANLIB_&lt;triple&gt;</c>, <c>CARGO_TARGET_&lt;TRIPLE&gt;_AR</c>),
/// and <c>BINDGEN_EXTRA_CLANG_ARGS_&lt;triple&gt;</c> gives a <c>bindgen</c> build script the
/// API-versioned clang target and the NDK sysroot (<see cref="BindgenClangArguments"/>).
/// These are the variables <c>cargo ndk</c> sets; cargo runs directly, so its JSON messages
/// (diagnostics, artifacts, progress) are not lost. The same type drives the Android layer of
/// CodeBrix.RustTools.Docker, so a container and a host set identical variables.
/// </summary>
/// <remarks>
/// The targets are <c>aarch64-linux-android</c> (ABI <c>arm64-v8a</c>) and
/// <c>x86_64-linux-android</c> (ABI <c>x86_64</c>); their standard libraries must be installed
/// (<c>rustup target add</c>). Crates that compile C work, because the NDK supplies the C
/// toolchain and headers. On Windows the NDK's wrappers are <c>.cmd</c> scripts and its
/// programs end in <c>.exe</c> (<see cref="WindowsFileNames"/>).
/// </remarks>
public sealed class AndroidLinkProfile
{
    /// <summary>The API level used when none is given: 24 (Android 7.0), the oldest level
    /// current NDKs support and the one CodeBrix.RustTools.Docker links for.</summary>
    public const int DefaultApiLevel = 24;

    /// <summary>The 64-bit ARM target triple (ABI <c>arm64-v8a</c>).</summary>
    public const string Arm64Triple = "aarch64-linux-android";

    /// <summary>The 64-bit Intel target triple (ABI <c>x86_64</c>).</summary>
    public const string X64Triple = "x86_64-linux-android";

    /// <summary>The two Android target triples the profile applies to.</summary>
    public static IReadOnlyList<string> Triples { get; } = Array.AsReadOnly(new[] { Arm64Triple, X64Triple });

    /// <summary>
    /// Creates a profile from a toolchain <c>bin</c> folder.
    /// </summary>
    /// <param name="toolchainBinDirectory">The NDK's <c>toolchains/llvm/prebuilt/&lt;host&gt;/bin</c>
    /// folder (a path on the machine that will build: a container path for a container).</param>
    /// <param name="apiLevel">The Android API level to link for.</param>
    /// <param name="windowsFileNames">Whether the tools carry Windows file names (<c>.cmd</c>
    /// wrappers, <c>llvm-ar.exe</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="toolchainBinDirectory"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="apiLevel"/> is below 21,
    /// the oldest level any 64-bit Android target has.</exception>
    public AndroidLinkProfile(string toolchainBinDirectory, int apiLevel = DefaultApiLevel, bool windowsFileNames = false)
    {
        if (string.IsNullOrWhiteSpace(toolchainBinDirectory))
        {
            throw new ArgumentException("The toolchain bin folder is required.", nameof(toolchainBinDirectory));
        }

        if (apiLevel < 21)
        {
            throw new ArgumentOutOfRangeException(nameof(apiLevel), apiLevel, "64-bit Android targets start at API level 21.");
        }

        ToolchainBinDirectory = toolchainBinDirectory.Trim().TrimEnd('/', '\\');
        ApiLevel = apiLevel;
        WindowsFileNames = windowsFileNames;
    }

    /// <summary>The toolchain <c>bin</c> folder.</summary>
    public string ToolchainBinDirectory { get; }

    /// <summary>The API level the wrappers link for.</summary>
    public int ApiLevel { get; }

    /// <summary>Whether the tools carry Windows file names.</summary>
    public bool WindowsFileNames { get; }

    /// <summary>The archiver (<c>llvm-ar</c>).</summary>
    public string ArchiverPath => Join(WindowsFileNames ? "llvm-ar.exe" : "llvm-ar");

    /// <summary>The <c>llvm-ranlib</c> tool.</summary>
    public string RanlibPath => Join(WindowsFileNames ? "llvm-ranlib.exe" : "llvm-ranlib");

    /// <summary>
    /// The profile for an NDK found on this machine.
    /// </summary>
    /// <param name="ndk">The NDK.</param>
    /// <param name="apiLevel">The Android API level to link for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ndk"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The NDK has no wrappers for the level.</exception>
    public static AndroidLinkProfile ForNdk(AndroidNdk ndk, int apiLevel = DefaultApiLevel)
    {
        if (ndk == null)
        {
            throw new ArgumentNullException(nameof(ndk));
        }

        if (ndk.ApiLevels.Count > 0 && !ndk.SupportsApiLevel(apiLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(apiLevel), apiLevel, $"{ndk} has wrappers for API levels {ndk.ApiLevels.Min()} to {ndk.ApiLevels.Max()} only.");
        }

        return new AndroidLinkProfile(ndk.ToolchainBinDirectory, apiLevel, ndk.WindowsFileNames);
    }

    /// <summary>
    /// Whether a triple is one of the Android targets this profile applies to.
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    public static bool IsAndroidTriple(string triple)
        => triple != null && Triples.Contains(triple.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Android ABI name of a target (<c>arm64-v8a</c>, <c>x86_64</c>), as used by the NDK and
    /// in an APK's <c>lib/</c> or <c>jniLibs/</c> folder.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not an Android target.</exception>
    public static string AbiOf(string triple) => Normalize(triple) switch
    {
        Arm64Triple => "arm64-v8a",
        X64Triple => "x86_64",
        _ => throw NotAndroid(triple),
    };

    /// <summary>
    /// The .NET runtime identifier of a target (<c>android-arm64</c>, <c>android-x64</c>), as
    /// used in a NuGet package's <c>runtimes/&lt;rid&gt;/native</c> folder.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not an Android target.</exception>
    public static string RuntimeIdentifierOf(string triple) => Normalize(triple) switch
    {
        Arm64Triple => "android-arm64",
        X64Triple => "android-x64",
        _ => throw NotAndroid(triple),
    };

    /// <summary>The form of a triple used in cargo's per-target variables
    /// (<c>AARCH64_LINUX_ANDROID</c>).</summary>
    /// <param name="triple">A Rust target triple.</param>
    public static string EnvironmentTripleName(string triple) => TargetTripleNames.Upper(triple);

    /// <summary>The <c>clang</c> wrapper for a target at this profile's API level.</summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    public string ClangPath(string triple) => Join(AndroidNdk.WrapperFileName(Normalize(triple), ApiLevel, "clang", WindowsFileNames));

    /// <summary>The <c>clang++</c> wrapper for a target at this profile's API level.</summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    public string ClangPlusPlusPath(string triple) => Join(AndroidNdk.WrapperFileName(Normalize(triple), ApiLevel, "clang++", WindowsFileNames));

    /// <summary>
    /// The environment variables that apply the profile to one target, in a fixed order:
    /// <c>CC_</c>, <c>CXX_</c>, <c>AR_</c>, <c>RANLIB_</c> (for the <c>cc</c> crate),
    /// <c>CARGO_TARGET_&lt;T&gt;_LINKER</c> and <c>CARGO_TARGET_&lt;T&gt;_AR</c> (for cargo).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not an Android target.</exception>
    public IReadOnlyDictionary<string, string> EnvironmentFor(string triple)
    {
        var normalized = Normalize(triple);
        if (!IsAndroidTriple(normalized))
        {
            throw NotAndroid(triple);
        }

        var lower = TargetTripleNames.Lower(normalized);
        var upper = TargetTripleNames.Upper(normalized);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CC_" + lower] = ClangPath(normalized),
            ["CXX_" + lower] = ClangPlusPlusPath(normalized),
            ["AR_" + lower] = ArchiverPath,
            ["RANLIB_" + lower] = RanlibPath,
            ["CARGO_TARGET_" + upper + "_LINKER"] = ClangPath(normalized),
            ["CARGO_TARGET_" + upper + "_AR"] = ArchiverPath,
            ["BINDGEN_EXTRA_CLANG_ARGS_" + lower] = BindgenClangArguments(normalized),
        };
    }

    /// <summary>
    /// The NDK's sysroot (the C library headers and libraries every Android target shares): the
    /// <c>sysroot</c> folder next to <see cref="ToolchainBinDirectory"/>.
    /// </summary>
    public string SysrootDirectory => Sibling(ToolchainBinDirectory, "sysroot");

    /// <summary>
    /// The extra libclang arguments a <c>bindgen</c> build script needs for a target, as
    /// <see cref="EnvironmentFor"/> puts them in <c>BINDGEN_EXTRA_CLANG_ARGS_&lt;triple&gt;</c>:
    /// the API-versioned clang target (<c>--target=aarch64-linux-android24</c>; recent NDKs
    /// refuse an unversioned Android triple with "Unversioned target triples are not
    /// supported") and the NDK sysroot. Only the per-target variable is set, never the plain
    /// <c>BINDGEN_EXTRA_CLANG_ARGS</c>, which would also reach the build scripts compiled for
    /// the host.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not an Android target.</exception>
    public string BindgenClangArguments(string triple)
    {
        var normalized = Normalize(triple);
        if (!IsAndroidTriple(normalized))
        {
            throw NotAndroid(triple);
        }

        return "--target=" + normalized + ApiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " --sysroot=" + BindgenPath(SysrootDirectory);
    }

    /// <summary>
    /// A path in the form bindgen's shell-style splitting of <c>BINDGEN_EXTRA_CLANG_ARGS</c>
    /// keeps intact: forward slashes (a backslash would be read as an escape; clang accepts
    /// forward slashes on Windows too), double-quoted when it contains a space.
    /// </summary>
    internal static string BindgenPath(string path)
    {
        var forward = (path ?? string.Empty).Replace('\\', '/');
        return forward.IndexOfAny(new[] { ' ', '\t', '"', '\'' }) >= 0
            ? "\"" + forward.Replace("\"", "\\\"") + "\""
            : forward;
    }

    /// <summary>The folder next to <paramref name="directory"/> called <paramref name="name"/>,
    /// joined with the directory's own separator.</summary>
    internal static string Sibling(string directory, string name)
    {
        var trimmed = directory.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        var separator = trimmed.Contains('\\') && !trimmed.Contains('/') ? "\\" : "/";
        return cut < 0 ? name : trimmed.Substring(0, cut) + separator + name;
    }

    /// <summary>
    /// Applies the profile to build options: sets <see cref="CargoBuildOptions.TargetTriple"/>
    /// and the variables of <see cref="EnvironmentFor"/> in
    /// <see cref="CargoBuildOptions.Environment"/>.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not an Android target.</exception>
    public void ApplyTo(CargoBuildOptions options, string triple)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var environment = EnvironmentFor(triple);
        options.TargetTriple = Normalize(triple);
        foreach (var pair in environment)
        {
            options.Environment[pair.Key] = pair.Value;
        }
    }

    private static string Normalize(string triple) => (triple ?? string.Empty).Trim().ToLowerInvariant();

    private static ArgumentException NotAndroid(string triple)
        => new ArgumentException($"'{triple}' is not an Android target triple ({string.Join(", ", Triples)}).", nameof(triple));

    /// <summary>Joins with the folder's own separator, so a Linux container path stays a Linux
    /// path when the profile is built on Windows.</summary>
    private string Join(string fileName)
        => ToolchainBinDirectory + (ToolchainBinDirectory.Contains('\\') && !ToolchainBinDirectory.Contains('/') ? "\\" : "/") + fileName;
}
