using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Engine;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// The facts about one <see cref="CrossTarget"/>: the Rust target triple, the matching .NET
/// runtime identifier, how its executables and libraries are named, and whether container
/// builds support it.
/// </summary>
public sealed class CrossTargetInfo
{
    /// <summary>
    /// The statement every macOS target carries in <see cref="Notes"/>.
    /// </summary>
    public const string MacOSDevelopmentOnlyNotice =
        "IMPORTANT NOTICE: macOS binaries produced by CodeBrix.RustTools.Docker are built in Docker containers without Apple's SDK, tools or files "
        + "and are for development and testing only. They must not be distributed or used in any production environment. "
        + "Any macOS build that will be distributed or run in production must be produced on a Mac and can be created using "
        + "CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are available under Apple's own license terms.";

    /// <summary>
    /// How the Windows targets are built: the <see cref="Notes"/> of
    /// <see cref="CrossTarget.WindowsX64Gnullvm"/> and <see cref="CrossTarget.WindowsArm64Gnullvm"/>.
    /// </summary>
    public const string WindowsGnullvmNotes =
        "Built with Rust's windows-gnullvm target and llvm-mingw (clang, lld and the mingw-w64 runtime: LLVM under Apache-2.0 with LLVM exceptions, mingw-w64 under its own permissive licences), "
        + "downloaded from github.com/mstorsjo/llvm-mingw when the Windows layer is built, for the engine's own architecture. The output links against Windows' Universal C Runtime "
        + "(Windows 10 and later) with llvm-mingw's unwinder linked in statically, so it imports only Windows' own DLLs. No Microsoft SDK, compiler or runtime is downloaded. "
        + "C ABI (P/Invoke works); the C++ ABI is Itanium, not MSVC's, so an MSVC-built .lib cannot be linked in.";

    /// <summary>
    /// How the macOS targets are built, appended to <see cref="MacOSDevelopmentOnlyNotice"/> in
    /// their <see cref="Notes"/>.
    /// </summary>
    public const string MacOSBuildNotes =
        " Built with rustup's rust-lld (ld64 flavour) against a libSystem symbol stub that the image derives from Rust's own precompiled standard library; "
        + "nothing from Apple is downloaded, copied or needed, on any engine architecture. Pure-Rust dependency trees only (a crate that compiles C needs Apple's headers). "
        + "The linker writes an ad-hoc signature. The same linker settings (CodeBrix.RustTools.MacOSLinkProfile) on a Mac give byte-for-byte the same files.";

    private static readonly IReadOnlyList<CrossTargetInfo> Table = Array.AsReadOnly(new[]
    {
        new CrossTargetInfo(
            CrossTarget.LinuxNative, string.Empty, string.Empty, null,
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: false,
            "The build container's own Linux architecture, resolved before anything is built to LinuxX64 (x86-64 engine), LinuxArm64 (ARM64 engine, for example Docker Desktop on Apple Silicon) or LinuxRiscV64 (RISC-V engine); "
            + "results and retrieved files carry the resolved target. The default target."),
        new CrossTargetInfo(
            CrossTarget.LinuxX64, "x86_64-unknown-linux-gnu", "linux-x64", null,
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: true,
            LinuxNotes("x86-64")),
        new CrossTargetInfo(
            CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu", "linux-arm64", null,
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: true,
            LinuxNotes("ARM64")),
        new CrossTargetInfo(
            CrossTarget.LinuxRiscV64, "riscv64gc-unknown-linux-gnu", "linux-riscv64", null,
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: true,
            LinuxNotes("RISC-V 64")),
        new CrossTargetInfo(
            CrossTarget.WindowsX64Gnullvm, "x86_64-pc-windows-gnullvm", "win-x64", null,
            ".exe", string.Empty, ".dll", "lib", ".a", isSupported: true, requiresLayer: true,
            WindowsGnullvmNotes),
        new CrossTargetInfo(
            CrossTarget.WindowsArm64Gnullvm, "aarch64-pc-windows-gnullvm", "win-arm64", null,
            ".exe", string.Empty, ".dll", "lib", ".a", isSupported: true, requiresLayer: true,
            WindowsGnullvmNotes),
        new CrossTargetInfo(
            CrossTarget.AndroidArm64, "aarch64-linux-android", "android-arm64", "arm64-v8a",
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: true,
            "Built with the Android NDK's clang (API level 24); needs an x86-64 Linux Docker engine. The NDK is downloaded from Google when the Android layer is built, at the consumer's request, and is licensed under the Android Software Development Kit License Agreement."),
        new CrossTargetInfo(
            CrossTarget.AndroidX64, "x86_64-linux-android", "android-x64", "x86_64",
            string.Empty, "lib", ".so", "lib", ".a", isSupported: true, requiresLayer: true,
            "Built with the Android NDK's clang (API level 24), mostly for emulators; needs an x86-64 Linux Docker engine. The NDK is downloaded from Google when the Android layer is built, at the consumer's request, and is licensed under the Android Software Development Kit License Agreement."),
        new CrossTargetInfo(
            CrossTarget.MacOSArm64, "aarch64-apple-darwin", "osx-arm64", null,
            string.Empty, "lib", ".dylib", "lib", ".a", isSupported: true, requiresLayer: true,
            MacOSDevelopmentOnlyNotice + MacOSBuildNotes),
        new CrossTargetInfo(
            CrossTarget.MacOSX64, "x86_64-apple-darwin", "osx-x64", null,
            string.Empty, "lib", ".dylib", "lib", ".a", isSupported: true, requiresLayer: true,
            MacOSDevelopmentOnlyNotice + MacOSBuildNotes),
        new CrossTargetInfo(
            CrossTarget.MacOSUniversal, "universal-apple-darwin", "osx", null,
            string.Empty, "lib", ".dylib", "lib", ".a", isSupported: true, requiresLayer: true,
            MacOSDevelopmentOnlyNotice + MacOSUniversalNotes + MacOSBuildNotes),
    });

    /// <summary>
    /// What <see cref="CrossTarget.MacOSUniversal"/> is, appended to
    /// <see cref="MacOSDevelopmentOnlyNotice"/> in its <see cref="Notes"/>.
    /// </summary>
    public const string MacOSUniversalNotes =
        " A universal (fat) file holding the aarch64-apple-darwin and x86_64-apple-darwin slices: BuildAsync builds both and joins every program and library "
        + "they share with CodeBrix.RustTools.MacOSUniversalBinary (no lipo involved); it is not a cargo target, so RunCargoAsync rejects it. "
        + "In the NuGet layout it lands in runtimes/osx/native, the portable macOS RID that osx-arm64 and osx-x64 fall back to.";

    private CrossTargetInfo(
        CrossTarget target,
        string triple,
        string runtimeIdentifier,
        string androidAbi,
        string executableExtension,
        string sharedLibraryPrefix,
        string sharedLibraryExtension,
        string staticLibraryPrefix,
        string staticLibraryExtension,
        bool isSupported,
        bool requiresLayer,
        string notes)
    {
        Target = target;
        Triple = triple;
        RuntimeIdentifier = runtimeIdentifier;
        AndroidAbi = androidAbi;
        ExecutableExtension = executableExtension;
        SharedLibraryPrefix = sharedLibraryPrefix;
        SharedLibraryExtension = sharedLibraryExtension;
        StaticLibraryPrefix = staticLibraryPrefix;
        StaticLibraryExtension = staticLibraryExtension;
        IsSupported = isSupported;
        RequiresLayer = requiresLayer;
        Notes = notes;
    }

    /// <summary>Every target, in <see cref="CrossTarget"/> order.</summary>
    public static IReadOnlyList<CrossTargetInfo> All => Table;

    /// <summary>The target this entry describes.</summary>
    public CrossTarget Target { get; }

    /// <summary>The Rust target triple, for example <c>x86_64-pc-windows-gnullvm</c>.</summary>
    public string Triple { get; }

    /// <summary>The .NET runtime identifier of the platform, for example <c>win-x64</c> or
    /// <c>android-arm64</c>, as used in a NuGet package's <c>runtimes/&lt;rid&gt;/native</c>
    /// folder.</summary>
    public string RuntimeIdentifier { get; }

    /// <summary>The Android ABI name (as used by the NDK and in an APK's <c>lib/</c> folder) (<c>arm64-v8a</c>, <c>x86_64</c>), or
    /// <c>null</c> for a non-Android target.</summary>
    public string AndroidAbi { get; }

    /// <summary>The file extension of an executable, including the dot (<c>.exe</c>), or an empty
    /// string when executables have none.</summary>
    public string ExecutableExtension { get; }

    /// <summary>The file-name prefix of a shared library (<c>lib</c> on Linux, Android and macOS,
    /// empty on Windows).</summary>
    public string SharedLibraryPrefix { get; }

    /// <summary>The file extension of a shared library, including the dot (<c>.so</c>,
    /// <c>.dll</c>, <c>.dylib</c>).</summary>
    public string SharedLibraryExtension { get; }

    /// <summary>The file-name prefix of a static library (<c>lib</c> for every supported target,
    /// including Windows GNU).</summary>
    public string StaticLibraryPrefix { get; }

    /// <summary>The file extension of a static library, including the dot (<c>.a</c>).</summary>
    public string StaticLibraryExtension { get; }

    /// <summary>Whether container builds can produce this target.</summary>
    public bool IsSupported { get; }

    /// <summary>Whether the build image needs an extra generated layer (a cross-compiler, an
    /// SDK) for this target.</summary>
    public bool RequiresLayer { get; }

    /// <summary>What to know about the target: how it is built, its licensing, its
    /// limits.</summary>
    public string Notes { get; }

    /// <summary>Whether this is one of the Android targets.</summary>
    public bool IsAndroid => AndroidAbi != null;

    /// <summary>Whether this is one of the Windows targets.</summary>
    public bool IsWindows => Target is CrossTarget.WindowsX64Gnullvm or CrossTarget.WindowsArm64Gnullvm;

    /// <summary>Whether this is one of the concrete Linux targets (<see cref="CrossTarget.LinuxX64"/>,
    /// <see cref="CrossTarget.LinuxArm64"/>, <see cref="CrossTarget.LinuxRiscV64"/>).</summary>
    public bool IsLinux => Target is CrossTarget.LinuxX64 or CrossTarget.LinuxArm64 or CrossTarget.LinuxRiscV64;

    /// <summary>Whether this is <see cref="CrossTarget.LinuxNative"/>, which is resolved to a
    /// concrete Linux target from the engine's architecture.</summary>
    public bool IsLinuxNative => Target == CrossTarget.LinuxNative;

    /// <summary>The GNU tool prefix of a concrete Linux target (<c>aarch64-linux-gnu</c>), used
    /// for its cross toolchain; <c>null</c> for every other target.</summary>
    public string GnuToolPrefix => IsLinux ? LinuxLinkProfile.ToolPrefixOf(Triple) : null;

    /// <summary>The Debian packages that provide a concrete Linux target's cross toolchain;
    /// empty for every other target.</summary>
    public IReadOnlyList<string> DebianCrossPackages => IsLinux ? LinuxLinkProfile.DebianPackagesOf(Triple) : Array.Empty<string>();

    /// <summary>
    /// Whether this target is the engine's own Linux architecture, so that it builds natively
    /// (no <c>--target</c>, no cross layer).
    /// </summary>
    /// <param name="engine">The engine.</param>
    public bool IsNativeOn(DockerEngineInfo engine)
        => IsLinux && engine != null && string.Equals(Triple, engine.NativeLinuxTriple, StringComparison.Ordinal);

    /// <summary>
    /// The concrete Linux target of an engine's own architecture, or <c>null</c> when the
    /// library has no target for it.
    /// </summary>
    /// <param name="engine">The engine.</param>
    public static CrossTarget? NativeLinuxTargetFor(DockerEngineInfo engine)
    {
        var triple = engine?.NativeLinuxTriple;
        return triple == null ? null : Table.FirstOrDefault(info => info.IsLinux && info.Triple == triple)?.Target;
    }

    /// <summary>
    /// Replaces <see cref="CrossTarget.LinuxNative"/> by the engine's own Linux target; any
    /// other target is returned as it is.
    /// </summary>
    /// <param name="target">The requested target.</param>
    /// <param name="engine">The engine that will build.</param>
    /// <exception cref="CrossTargetNotSupportedException">The engine's architecture has no
    /// Linux target in this library.</exception>
    public static CrossTarget Resolve(CrossTarget target, DockerEngineInfo engine)
    {
        if (target != CrossTarget.LinuxNative)
        {
            return target;
        }

        return NativeLinuxTargetFor(engine)
            ?? throw new CrossTargetNotSupportedException(
                target,
                $"LinuxNative cannot be resolved: the engine's architecture ({engine?.Architecture ?? "unknown"}) has no Linux target in this library (x86_64, aarch64 and riscv64 do).");
    }

    private static string LinuxNotes(string architecture)
        => $"64-bit {architecture} Linux (GNU libc). Native on an engine of that architecture; on any other engine it is cross-compiled with Debian's cross GCC, g++ and glibc packages "
           + "(the image layer installs them and the rustup target), which also serve crates that compile C. Binaries run on glibc-based distributions.";

    /// <summary>Whether this is one of the macOS targets, the universal file included.</summary>
    public bool IsMacOS => Target == CrossTarget.MacOSArm64 || Target == CrossTarget.MacOSX64 || Target == CrossTarget.MacOSUniversal;

    /// <summary>Whether this is <see cref="CrossTarget.MacOSUniversal"/>, which is retrieved, not
    /// compiled: cargo builds its two slices.</summary>
    public bool IsMacOSUniversal => Target == CrossTarget.MacOSUniversal;

    /// <summary>The single-architecture targets that <see cref="CrossTarget.MacOSUniversal"/>
    /// is made of, in file order.</summary>
    public static IReadOnlyList<CrossTarget> MacOSUniversalSlices { get; } = Array.AsReadOnly(new[] { CrossTarget.MacOSArm64, CrossTarget.MacOSX64 });

    /// <summary>
    /// Returns the entry for a target.
    /// </summary>
    /// <param name="target">The target to describe.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is not a defined
    /// value.</exception>
    public static CrossTargetInfo Get(CrossTarget target)
        => Table.FirstOrDefault(info => info.Target == target)
           ?? throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown cross-compilation target.");

    /// <summary>
    /// Finds the entry whose <see cref="Triple"/> matches (case-insensitively).
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    /// <param name="info">The matching entry, or <c>null</c>.</param>
    /// <returns><c>true</c> when a target matched.</returns>
    public static bool TryFromTriple(string triple, out CrossTargetInfo info)
    {
        info = string.IsNullOrWhiteSpace(triple)
            ? null
            : Table.FirstOrDefault(entry => string.Equals(entry.Triple, triple.Trim(), StringComparison.OrdinalIgnoreCase));
        return info != null;
    }

    /// <summary>
    /// The file name of an executable called <paramref name="name"/> on this target, for example
    /// <c>hello.exe</c> on Windows.
    /// </summary>
    /// <param name="name">The binary target's name.</param>
    public string ExecutableFileName(string name) => name + ExecutableExtension;

    /// <summary>
    /// The file name of a shared library built from the crate <paramref name="crateName"/>
    /// (hyphens become underscores, as cargo does), for example <c>libmy_lib.so</c> or
    /// <c>my_lib.dll</c>.
    /// </summary>
    /// <param name="crateName">The library target's name.</param>
    public string SharedLibraryFileName(string crateName)
        => SharedLibraryPrefix + (crateName ?? string.Empty).Replace('-', '_') + SharedLibraryExtension;

    /// <summary>
    /// The file name of a static library built from the crate <paramref name="crateName"/>, for
    /// example <c>libmy_lib.a</c>.
    /// </summary>
    /// <param name="crateName">The library target's name.</param>
    public string StaticLibraryFileName(string crateName)
        => StaticLibraryPrefix + (crateName ?? string.Empty).Replace('-', '_') + StaticLibraryExtension;

    /// <summary>Returns the target name and triple.</summary>
    public override string ToString() => $"{Target} ({Triple})";

    /// <summary>
    /// Throws when the engine cannot build one of the targets: Android targets need an x86-64
    /// Linux engine, because the Android NDK ships its compilers and linkers for x86-64 Linux
    /// hosts only.
    /// </summary>
    /// <param name="engine">The engine that would run the build.</param>
    /// <param name="targets">The requested targets.</param>
    /// <exception cref="CrossTargetNotSupportedException">An Android target was requested and
    /// the engine is not x86-64.</exception>
    internal static void EnsureEngineCanBuild(DockerEngineInfo engine, IEnumerable<CrossTarget> targets)
    {
        if (engine == null || targets == null || IsX64Architecture(engine.Architecture))
        {
            return;
        }

        foreach (var target in targets)
        {
            if (Get(target).IsAndroid)
            {
                var architecture = string.IsNullOrWhiteSpace(engine.Architecture) ? "unknown" : engine.Architecture;
                throw new CrossTargetNotSupportedException(
                    target,
                    $"Android cross-compilation ({target}) needs an x86-64 Linux Docker engine, because the Android NDK ships its compilers for x86-64 Linux only; "
                    + $"this engine's architecture is {architecture}. On Docker Desktop for Apple Silicon that would mean an amd64 build image running under emulation, "
                    + "which CodeBrix.RustTools.Docker does not set up.");
            }
        }
    }

    private static bool IsX64Architecture(string architecture)
        => string.Equals(architecture, "x86_64", StringComparison.OrdinalIgnoreCase)
           || string.Equals(architecture, "amd64", StringComparison.OrdinalIgnoreCase);
}
