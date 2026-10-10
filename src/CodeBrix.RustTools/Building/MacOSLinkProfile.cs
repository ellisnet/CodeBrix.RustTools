using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The linker settings that build a macOS target (<c>aarch64-apple-darwin</c> or
/// <c>x86_64-apple-darwin</c>) with rustup's own linker, <c>rust-lld</c> in its ld64 flavour,
/// against a libSystem symbol stub, instead of Apple's linker and SDK. The same settings work
/// on a Mac and inside a Linux container, which is what makes a Mac build and a
/// CodeBrix.RustTools.Docker container build of the same source byte-for-byte identical (same
/// compiler release, same linker, same stub, same flags).
/// </summary>
/// <remarks>
/// <para>What the profile sets, per target triple: the linker (<c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c>
/// = <see cref="RustLldPath"/>), the rustflags (<c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c> =
/// <see cref="RustFlags"/>: the ld64.lld linker flavour, <c>-S</c> so that no debug map with
/// object-file paths and timestamps enters the output, the remapping of the cargo registry
/// source folder to <see cref="RegistryRemapTarget"/>, and the install name when one is given),
/// <c>SDKROOT</c> = <see cref="SysrootDirectory"/> (rustc then passes <c>-syslibroot</c> itself
/// and does not look for Xcode), and <c>MACOSX_DEPLOYMENT_TARGET</c> = <see cref="DeploymentTarget"/>
/// (fixed on both sides, so the platform version recorded in the binary is the same).</para>
/// <para>The sysroot holds only <c>usr/lib/libSystem.tbd</c> (plus the <c>libc</c>, <c>libm</c>,
/// <c>libpthread</c> and <c>libdl</c> aliases of it): a text stub listing the symbol names Rust's
/// standard library imports from <c>/usr/lib/libSystem.B.dylib</c>, derived from rustup's own
/// files, plus the ISO C math functions and the Darwin library calls LLVM emits for float and
/// memory operations (<c>pow</c>, <c>log</c>, <c>sin</c>, <c>fmod</c>, <c>__sincos_stret</c>, ...;
/// names only), so that crates doing float math link. A CodeBrix.RustTools.Docker image with a
/// macOS target generates it, and the container workbench can copy it to the host for this
/// profile. Nothing from Apple is involved on either side.</para>
/// <para>IMPORTANT NOTICE: binaries built this way carry an ad-hoc signature written by the
/// linker and are for development and testing only; they must not be distributed or used in any
/// production environment. Any macOS build that will be is produced on a Mac with Apple's
/// toolchain, SDK and signing identity, which is what this library does without the profile.
/// Pure-Rust dependency trees only: a crate that
/// compiles C needs Apple's headers. Variables set by the profile override the same variables
/// in <see cref="CargoBuildOptions.Environment"/>; a <c>RUSTFLAGS</c> variable set by the caller
/// would in turn override the profile's target rustflags, so do not set one.</para>
/// </remarks>
public sealed class MacOSLinkProfile
{
    /// <summary>The <c>MACOSX_DEPLOYMENT_TARGET</c> the profile fixes for both macOS targets.</summary>
    public const string DeploymentTarget = "11.0";

    /// <summary>The path the cargo registry source folder is remapped to in panic locations and
    /// debug information, so that the folder's location on the building machine does not enter
    /// the binary.</summary>
    public const string RegistryRemapTarget = "/cargo-registry-src";

    /// <summary>The stub file inside a sysroot folder, relative to it.</summary>
    public const string StubRelativePath = "usr/lib/libSystem.tbd";

    /// <summary>The Apple Silicon target triple.</summary>
    public const string Arm64Triple = "aarch64-apple-darwin";

    /// <summary>The Intel target triple.</summary>
    public const string X64Triple = "x86_64-apple-darwin";

    /// <summary>The two macOS target triples the profile applies to.</summary>
    public static IReadOnlyList<string> Triples { get; } = Array.AsReadOnly(new[] { Arm64Triple, X64Triple });

    /// <summary>
    /// Creates a profile.
    /// </summary>
    /// <param name="rustLldPath">The <c>rust-lld</c> executable of the toolchain that will build
    /// (under the sysroot's <c>lib/rustlib/&lt;host&gt;/bin</c>).</param>
    /// <param name="sysrootDirectory">The folder that holds <see cref="StubRelativePath"/>.</param>
    /// <param name="cargoRegistrySourceDirectory">The cargo registry source folder of the
    /// building machine (<c>&lt;CARGO_HOME&gt;/registry/src</c>), remapped to
    /// <see cref="RegistryRemapTarget"/>; <c>null</c> for no remapping.</param>
    /// <exception cref="ArgumentException">A required path is blank.</exception>
    public MacOSLinkProfile(string rustLldPath, string sysrootDirectory, string cargoRegistrySourceDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(rustLldPath))
        {
            throw new ArgumentException("The rust-lld path is required.", nameof(rustLldPath));
        }

        if (string.IsNullOrWhiteSpace(sysrootDirectory))
        {
            throw new ArgumentException("The sysroot folder is required.", nameof(sysrootDirectory));
        }

        RustLldPath = rustLldPath.Trim();
        SysrootDirectory = sysrootDirectory.Trim();
        CargoRegistrySourceDirectory = string.IsNullOrWhiteSpace(cargoRegistrySourceDirectory) ? null : cargoRegistrySourceDirectory.Trim();
    }

    /// <summary>The linker: rustup's <c>rust-lld</c>.</summary>
    public string RustLldPath { get; }

    /// <summary>The sysroot folder holding the libSystem stub; passed as <c>SDKROOT</c>.</summary>
    public string SysrootDirectory { get; }

    /// <summary>The cargo registry source folder that is remapped, or <c>null</c>.</summary>
    public string CargoRegistrySourceDirectory { get; }

    /// <summary>The full path of the stub file this profile links against.</summary>
    public string StubPath => Path.Combine(SysrootDirectory, StubRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Whether a triple is one of the macOS targets this profile applies to.
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    public static bool IsMacOSTriple(string triple)
        => triple != null && Triples.Contains(triple.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The form of a triple used in cargo's per-target environment variables
    /// (<c>aarch64-apple-darwin</c> becomes <c>AARCH64_APPLE_DARWIN</c>).
    /// </summary>
    /// <param name="triple">A Rust target triple.</param>
    public static string EnvironmentTripleName(string triple) => TargetTripleNames.Upper(triple);

    /// <summary>
    /// The rustflags the profile passes for a macOS target, in order.
    /// </summary>
    /// <param name="installName">The install name for a cdylib (for example
    /// <c>@rpath/libmy_lib.dylib</c>), or <c>null</c> for the linker default.</param>
    public IReadOnlyList<string> RustFlags(string installName = null)
    {
        var flags = new List<string> { "-C", "linker-flavor=ld64.lld", "-C", "link-arg=-S" };
        if (CargoRegistrySourceDirectory != null)
        {
            flags.Add("--remap-path-prefix=" + CargoRegistrySourceDirectory + "=" + RegistryRemapTarget);
        }

        if (!string.IsNullOrWhiteSpace(installName))
        {
            flags.Add("-C");
            flags.Add("link-arg=-install_name");
            flags.Add("-C");
            flags.Add("link-arg=" + installName.Trim());
        }

        return flags;
    }

    /// <summary>
    /// The rustflags as the single space-separated value cargo reads from
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c>. Paths that contain spaces cannot be expressed
    /// in that variable.
    /// </summary>
    /// <param name="installName">See <see cref="RustFlags"/>.</param>
    public string RustFlagsText(string installName = null) => string.Join(" ", RustFlags(installName));

    /// <summary>
    /// The environment variables that apply the profile to one macOS target.
    /// </summary>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <param name="installName">See <see cref="RustFlags"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a macOS target.</exception>
    public IReadOnlyDictionary<string, string> EnvironmentFor(string triple, string installName = null)
    {
        if (!IsMacOSTriple(triple))
        {
            throw new ArgumentException($"'{triple}' is not a macOS target triple ({string.Join(", ", Triples)}).", nameof(triple));
        }

        var name = EnvironmentTripleName(triple);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CARGO_TARGET_" + name + "_LINKER"] = RustLldPath,
            ["CARGO_TARGET_" + name + "_RUSTFLAGS"] = RustFlagsText(installName),
            ["SDKROOT"] = SysrootDirectory,
            ["MACOSX_DEPLOYMENT_TARGET"] = DeploymentTarget,
        };
    }

    /// <summary>
    /// Applies the profile to build options: sets <see cref="CargoBuildOptions.TargetTriple"/>,
    /// <see cref="CargoBuildOptions.MacOSInstallName"/> (when an install name is given) and the
    /// variables of <see cref="EnvironmentFor"/> in <see cref="CargoBuildOptions.Environment"/>.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="triple">One of <see cref="Triples"/>.</param>
    /// <param name="installName">See <see cref="RustFlags"/>; <c>null</c> keeps
    /// <see cref="CargoBuildOptions.MacOSInstallName"/> as it is and uses it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a macOS target.</exception>
    public void ApplyTo(CargoBuildOptions options, string triple, string installName = null)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(installName))
        {
            options.MacOSInstallName = installName.Trim();
        }

        var environment = EnvironmentFor(triple, options.MacOSInstallName);
        options.TargetTriple = triple.Trim();
        foreach (var pair in environment)
        {
            options.Environment[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Creates the profile for the toolchain installed on this machine: <c>rust-lld</c> from the
    /// toolchain's sysroot, the cargo registry folder from its <c>CARGO_HOME</c>.
    /// </summary>
    /// <param name="toolchain">A detection result with <see cref="RustToolchainInfo.Sysroot"/>
    /// and <see cref="RustToolchainInfo.HostTriple"/>.</param>
    /// <param name="sysrootDirectory">The folder that holds the libSystem stub (for example one
    /// copied out of a CodeBrix.RustTools.Docker container).</param>
    /// <exception cref="ArgumentNullException"><paramref name="toolchain"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The toolchain has no sysroot or host triple, or
    /// <paramref name="sysrootDirectory"/> is blank.</exception>
    /// <exception cref="FileNotFoundException"><c>rust-lld</c> is not in the toolchain, or the
    /// sysroot folder has no stub.</exception>
    public static MacOSLinkProfile ForToolchain(RustToolchainInfo toolchain, string sysrootDirectory)
    {
        if (toolchain == null)
        {
            throw new ArgumentNullException(nameof(toolchain));
        }

        if (string.IsNullOrWhiteSpace(toolchain.Sysroot) || string.IsNullOrWhiteSpace(toolchain.HostTriple))
        {
            throw new ArgumentException("The toolchain's sysroot and host triple are needed to find rust-lld; detect the toolchain first.", nameof(toolchain));
        }

        var rustLld = Path.Combine(toolchain.Sysroot, "lib", "rustlib", toolchain.HostTriple, "bin", OperatingSystem.IsWindows() ? "rust-lld.exe" : "rust-lld");
        if (!File.Exists(rustLld))
        {
            throw new FileNotFoundException("rust-lld was not found in the toolchain; it is part of every rustup toolchain since Rust 1.0.", rustLld);
        }

        var profile = new MacOSLinkProfile(
            rustLld,
            sysrootDirectory,
            string.IsNullOrWhiteSpace(toolchain.CargoHome) ? null : Path.Combine(toolchain.CargoHome, "registry", "src"));
        if (!File.Exists(profile.StubPath))
        {
            throw new FileNotFoundException("The sysroot folder holds no libSystem stub.", profile.StubPath);
        }

        return profile;
    }

    /// <summary>
    /// The conventional install name for the package's cdylib, <c>@rpath/lib&lt;name&gt;.dylib</c>
    /// (hyphens in the library target's name become underscores, as cargo does), when the
    /// package declares exactly one library target with the <c>cdylib</c> crate type; otherwise
    /// <c>null</c>. A workspace root with no package gives <c>null</c>.
    /// </summary>
    /// <param name="project">The project to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is <c>null</c>.</exception>
    public static string DefaultInstallName(CargoProject project)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        var cdylibs = project.GetDeclaredTargets()
            .Where(t => t.CrateTypes.Any(c => string.Equals(c, "cdylib", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return cdylibs.Count == 1 && !string.IsNullOrWhiteSpace(cdylibs[0].Name)
            ? "@rpath/lib" + cdylibs[0].Name.Replace('-', '_') + ".dylib"
            : null;
    }
}
