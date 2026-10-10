using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The settings that build Rust's <c>*-pc-windows-gnullvm</c> targets (x64 and ARM64 Windows)
/// with llvm-mingw: clang, lld and the mingw-w64 runtime, all open source (LLVM under Apache-2.0
/// with LLVM exceptions, mingw-w64 under its own permissive licences). The target's clang
/// wrapper becomes cargo's linker and the <c>cc</c> crate's compiler, <c>llvm-ar</c> the
/// archiver, and <see cref="RustFlags"/> makes rustc bundle llvm-mingw's unwinder statically, so
/// the output imports nothing but Windows' own DLLs: the Universal C Runtime (Windows 10 and
/// later) and the system libraries. No Microsoft SDK, compiler or runtime is involved. The same
/// type drives the Windows layer of CodeBrix.RustTools.Docker.
/// </summary>
/// <remarks>
/// The targets are <c>x86_64-pc-windows-gnullvm</c> (RID <c>win-x64</c>) and
/// <c>aarch64-pc-windows-gnullvm</c> (RID <c>win-arm64</c>); their standard libraries must be
/// installed (<c>rustup target add</c>). Programs and DLLs use the C ABI (P/Invoke works) and
/// the Itanium C++ ABI, not MSVC's, so an MSVC-built <c>.lib</c> cannot be linked in. On a
/// Windows host llvm-mingw's tools end in <c>.exe</c> (<see cref="WindowsFileNames"/>).
/// </remarks>
public sealed class WindowsGnullvmLinkProfile
{
    /// <summary>The 64-bit x86 target triple.</summary>
    public const string X64Triple = "x86_64-pc-windows-gnullvm";

    /// <summary>The 64-bit ARM target triple.</summary>
    public const string Arm64Triple = "aarch64-pc-windows-gnullvm";

    /// <summary>The rustflags the profile passes: <c>+crt-static</c> makes rustc link
    /// llvm-mingw's unwinder (and its other runtime pieces) statically instead of importing
    /// <c>libunwind.dll</c>; Windows' Universal C Runtime stays dynamic.</summary>
    public const string RustFlags = "-C target-feature=+crt-static";

    /// <summary>The two Windows target triples the profile applies to.</summary>
    public static IReadOnlyList<string> Triples { get; } = Array.AsReadOnly(new[] { X64Triple, Arm64Triple });

    /// <summary>
    /// Creates a profile from llvm-mingw's <c>bin</c> folder.
    /// </summary>
    /// <param name="binDirectory">The folder that holds <c>x86_64-w64-mingw32-clang</c>,
    /// <c>aarch64-w64-mingw32-clang</c>, <c>llvm-ar</c> and <c>llvm-ranlib</c> (a path on the
    /// machine that will build: a container path for a container).</param>
    /// <param name="windowsFileNames">Whether the tools end in <c>.exe</c> (an llvm-mingw
    /// build for Windows hosts).</param>
    /// <exception cref="ArgumentException"><paramref name="binDirectory"/> is blank.</exception>
    public WindowsGnullvmLinkProfile(string binDirectory, bool windowsFileNames = false)
    {
        if (string.IsNullOrWhiteSpace(binDirectory))
        {
            throw new ArgumentException("The llvm-mingw bin folder is required.", nameof(binDirectory));
        }

        BinDirectory = binDirectory.Trim().TrimEnd('/', '\\');
        WindowsFileNames = windowsFileNames;
    }

    /// <summary>llvm-mingw's <c>bin</c> folder.</summary>
    public string BinDirectory { get; }

    /// <summary>Whether the tools end in <c>.exe</c>.</summary>
    public bool WindowsFileNames { get; }

    /// <summary>The archiver, <c>llvm-ar</c>.</summary>
    public string ArchiverPath => Join("llvm-ar" + Suffix);

    /// <summary>The <c>llvm-ranlib</c> tool.</summary>
    public string RanlibPath => Join("llvm-ranlib" + Suffix);

    /// <summary>
    /// Whether a triple is one of the Windows gnullvm targets.
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    public static bool IsWindowsGnullvmTriple(string triple)
        => triple != null && Triples.Contains(triple.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The machine part of a target's tool names (<c>x86_64</c> or <c>aarch64</c>, as in
    /// <c>aarch64-w64-mingw32-clang</c>).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static string MachineOf(string triple) => Normalize(triple) switch
    {
        X64Triple => "x86_64",
        Arm64Triple => "aarch64",
        _ => throw NotWindows(triple),
    };

    /// <summary>
    /// The .NET runtime identifier of a target (<c>win-x64</c>, <c>win-arm64</c>).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static string RuntimeIdentifierOf(string triple) => Normalize(triple) switch
    {
        X64Triple => "win-x64",
        Arm64Triple => "win-arm64",
        _ => throw NotWindows(triple),
    };

    /// <summary>The <c>clang</c> wrapper for a target (<c>&lt;machine&gt;-w64-mingw32-clang</c>).</summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    public string ClangPath(string triple) => Join(MachineOf(triple) + "-w64-mingw32-clang" + Suffix);

    /// <summary>The <c>clang++</c> wrapper for a target.</summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    public string ClangPlusPlusPath(string triple) => Join(MachineOf(triple) + "-w64-mingw32-clang++" + Suffix);

    /// <summary>
    /// The environment variables that apply the profile to a target, in a fixed order:
    /// <c>CC_</c>, <c>CXX_</c>, <c>AR_</c>, <c>RANLIB_</c> (for the <c>cc</c> crate),
    /// <c>CARGO_TARGET_&lt;T&gt;_LINKER</c>, <c>CARGO_TARGET_&lt;T&gt;_AR</c> and
    /// <c>CARGO_TARGET_&lt;T&gt;_RUSTFLAGS</c> (<see cref="RustFlags"/>).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Windows gnullvm target.</exception>
    public IReadOnlyDictionary<string, string> EnvironmentFor(string triple)
    {
        var normalized = Normalize(triple);
        var machine = MachineOf(normalized);
        var lower = TargetTripleNames.Lower(normalized);
        var upper = TargetTripleNames.Upper(normalized);
        var clang = Join(machine + "-w64-mingw32-clang" + Suffix);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CC_" + lower] = clang,
            ["CXX_" + lower] = Join(machine + "-w64-mingw32-clang++" + Suffix),
            ["AR_" + lower] = ArchiverPath,
            ["RANLIB_" + lower] = RanlibPath,
            ["CARGO_TARGET_" + upper + "_LINKER"] = clang,
            ["CARGO_TARGET_" + upper + "_AR"] = ArchiverPath,
            ["CARGO_TARGET_" + upper + "_RUSTFLAGS"] = RustFlags,
        };
    }

    /// <summary>
    /// Applies the profile to build options: sets <see cref="CargoBuildOptions.TargetTriple"/>
    /// and the variables of <see cref="EnvironmentFor"/> in
    /// <see cref="CargoBuildOptions.Environment"/>. A <c>RUSTFLAGS</c> variable set by the
    /// caller would override the profile's rustflags, so do not set one.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Windows gnullvm target.</exception>
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

    private string Suffix => WindowsFileNames ? ".exe" : string.Empty;

    private static string Normalize(string triple) => (triple ?? string.Empty).Trim().ToLowerInvariant();

    private static ArgumentException NotWindows(string triple)
        => new ArgumentException($"'{triple}' is not a Windows gnullvm target triple ({string.Join(", ", Triples)}).", nameof(triple));

    private string Join(string fileName)
        => BinDirectory + (BinDirectory.Contains('\\') && !BinDirectory.Contains('/') ? "\\" : "/") + fileName;
}
