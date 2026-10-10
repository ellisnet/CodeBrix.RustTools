using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The settings that build a Linux target of another processor architecture with a GNU cross
/// toolchain, such as Debian's <c>gcc-aarch64-linux-gnu</c>: the cross <c>gcc</c> becomes
/// cargo's linker (<c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c>) and the <c>cc</c> crate's compiler
/// (<c>CC_&lt;triple&gt;</c>, <c>CXX_&lt;triple&gt;</c>), and the cross <c>ar</c> the archiver.
/// Because the cross packages bring glibc's headers for the target, crates that compile C work.
/// The same type drives the Linux cross layers of CodeBrix.RustTools.Docker, so a container and
/// a host set identical variables.
/// </summary>
/// <remarks>
/// The targets are <c>x86_64-unknown-linux-gnu</c>, <c>aarch64-unknown-linux-gnu</c> and
/// <c>riscv64gc-unknown-linux-gnu</c>; the target's standard library must be installed
/// (<c>rustup target add</c>) and the tools (<see cref="ToolPrefix"/>-gcc, -g++, -ar, -ranlib)
/// on PATH or in <see cref="BinDirectory"/>. On a Debian-family host
/// <see cref="DebianPackagesOf"/> names the packages to install.
/// </remarks>
public sealed class LinuxLinkProfile
{
    /// <summary>The 64-bit x86 target triple.</summary>
    public const string X64Triple = "x86_64-unknown-linux-gnu";

    /// <summary>The 64-bit ARM target triple.</summary>
    public const string Arm64Triple = "aarch64-unknown-linux-gnu";

    /// <summary>The 64-bit RISC-V target triple (the <c>gc</c> extension set every Linux
    /// distribution builds for).</summary>
    public const string RiscV64Triple = "riscv64gc-unknown-linux-gnu";

    /// <summary>The Linux target triples the profile applies to.</summary>
    public static IReadOnlyList<string> Triples { get; } = Array.AsReadOnly(new[] { X64Triple, Arm64Triple, RiscV64Triple });

    private static readonly IReadOnlyDictionary<string, (string Prefix, string Rid, string DebianArch)> Table = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
    {
        [X64Triple] = ("x86_64-linux-gnu", "linux-x64", "amd64"),
        [Arm64Triple] = ("aarch64-linux-gnu", "linux-arm64", "arm64"),
        [RiscV64Triple] = ("riscv64-linux-gnu", "linux-riscv64", "riscv64"),
    };

    /// <summary>
    /// Creates a profile for a GNU tool prefix.
    /// </summary>
    /// <param name="toolPrefix">The prefix of the cross tools, for example
    /// <c>aarch64-linux-gnu</c> (the tools are <c>&lt;prefix&gt;-gcc</c>, <c>-g++</c>, <c>-ar</c>,
    /// <c>-ranlib</c>).</param>
    /// <param name="binDirectory">The folder that holds the tools, or <c>null</c> (the
    /// default) when they are on PATH.</param>
    /// <exception cref="ArgumentException"><paramref name="toolPrefix"/> is blank.</exception>
    public LinuxLinkProfile(string toolPrefix, string binDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(toolPrefix))
        {
            throw new ArgumentException("The tool prefix is required.", nameof(toolPrefix));
        }

        ToolPrefix = toolPrefix.Trim();
        BinDirectory = string.IsNullOrWhiteSpace(binDirectory) ? null : binDirectory.Trim().TrimEnd('/', '\\');
    }

    /// <summary>The GNU tool prefix (<c>aarch64-linux-gnu</c>).</summary>
    public string ToolPrefix { get; }

    /// <summary>The folder that holds the tools, or <c>null</c> for PATH.</summary>
    public string BinDirectory { get; }

    /// <summary>The C compiler and linker driver (<c>&lt;prefix&gt;-gcc</c>).</summary>
    public string CompilerPath => Join(ToolPrefix + "-gcc");

    /// <summary>The C++ compiler (<c>&lt;prefix&gt;-g++</c>).</summary>
    public string CxxCompilerPath => Join(ToolPrefix + "-g++");

    /// <summary>The archiver (<c>&lt;prefix&gt;-ar</c>).</summary>
    public string ArchiverPath => Join(ToolPrefix + "-ar");

    /// <summary>The <c>ranlib</c> tool (<c>&lt;prefix&gt;-ranlib</c>).</summary>
    public string RanlibPath => Join(ToolPrefix + "-ranlib");

    /// <summary>
    /// Whether a triple is one of the Linux targets this profile knows.
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    public static bool IsLinuxTriple(string triple) => triple != null && Table.ContainsKey(triple.Trim());

    /// <summary>
    /// The GNU tool prefix of a target (<c>x86_64-linux-gnu</c>, <c>aarch64-linux-gnu</c>,
    /// <c>riscv64-linux-gnu</c>).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static string ToolPrefixOf(string triple) => Entry(triple).Prefix;

    /// <summary>
    /// The .NET runtime identifier of a target (<c>linux-x64</c>, <c>linux-arm64</c>,
    /// <c>linux-riscv64</c>).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static string RuntimeIdentifierOf(string triple) => Entry(triple).Rid;

    /// <summary>
    /// The Debian packages that provide the cross toolchain for a target: the cross
    /// <c>gcc</c> and <c>g++</c> and the target's glibc headers and libraries.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static IReadOnlyList<string> DebianPackagesOf(string triple)
    {
        var entry = Entry(triple);
        var gnu = entry.Prefix.Replace("x86_64", "x86-64");
        return new[] { "gcc-" + gnu, "g++-" + gnu, "libc6-dev-" + entry.DebianArch + "-cross" };
    }

    /// <summary>
    /// The profile for a target, with the GNU prefix the target uses.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <param name="binDirectory">The folder that holds the tools, or <c>null</c> for PATH.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not one of them.</exception>
    public static LinuxLinkProfile ForTriple(string triple, string binDirectory = null) => new LinuxLinkProfile(ToolPrefixOf(triple), binDirectory);

    /// <summary>
    /// The environment variables that apply the profile to a target, in a fixed order:
    /// <c>CC_</c>, <c>CXX_</c>, <c>AR_</c>, <c>RANLIB_</c> (for the <c>cc</c> crate),
    /// <c>CARGO_TARGET_&lt;T&gt;_LINKER</c> and <c>CARGO_TARGET_&lt;T&gt;_AR</c> (for cargo).
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>, whose prefix is this profile's.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Linux target of
    /// this profile's prefix.</exception>
    public IReadOnlyDictionary<string, string> EnvironmentFor(string triple)
    {
        var normalized = (triple ?? string.Empty).Trim();
        if (!string.Equals(ToolPrefixOf(normalized), ToolPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{normalized}' is built with the {ToolPrefixOf(normalized)} tools, not {ToolPrefix}.", nameof(triple));
        }

        var lower = TargetTripleNames.Lower(normalized);
        var upper = TargetTripleNames.Upper(normalized);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CC_" + lower] = CompilerPath,
            ["CXX_" + lower] = CxxCompilerPath,
            ["AR_" + lower] = ArchiverPath,
            ["RANLIB_" + lower] = RanlibPath,
            ["CARGO_TARGET_" + upper + "_LINKER"] = CompilerPath,
            ["CARGO_TARGET_" + upper + "_AR"] = ArchiverPath,
        };
    }

    /// <summary>
    /// Applies the profile to build options: sets <see cref="CargoBuildOptions.TargetTriple"/>
    /// and the variables of <see cref="EnvironmentFor"/> in
    /// <see cref="CargoBuildOptions.Environment"/>.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="triple">One of <see cref="Triples"/>, whose prefix is this profile's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="triple"/> does not fit the profile.</exception>
    public void ApplyTo(CargoBuildOptions options, string triple)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var environment = EnvironmentFor(triple);
        options.TargetTriple = triple.Trim();
        foreach (var pair in environment)
        {
            options.Environment[pair.Key] = pair.Value;
        }
    }

    private static (string Prefix, string Rid, string DebianArch) Entry(string triple)
        => triple != null && Table.TryGetValue(triple.Trim(), out var entry)
            ? entry
            : throw new ArgumentException($"'{triple}' is not a Linux target triple this profile knows ({string.Join(", ", Triples)}).", nameof(triple));

    private string Join(string fileName)
        => BinDirectory == null
            ? fileName
            : BinDirectory + (BinDirectory.Contains('\\') && !BinDirectory.Contains('/') ? "\\" : "/") + fileName;
}
