using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Licensing;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Options for one cargo command run by <see cref="CargoRunner"/>. Each option maps to a cargo
/// flag; options that a subcommand does not support are left out of its command line (for
/// example <see cref="Features"/> is not passed to <c>cargo clean</c> or <c>cargo fmt</c>).
/// </summary>
public sealed class CargoBuildOptions
{
    /// <summary>
    /// The built-in profile: <see cref="CargoProfile.Debug"/> (the default, no flag) or
    /// <see cref="CargoProfile.Release"/> (<c>--release</c>). Ignored when
    /// <see cref="CustomProfileName"/> is set.
    /// </summary>
    public CargoProfile Profile { get; set; } = CargoProfile.Debug;

    /// <summary>
    /// A named profile from the manifest's <c>[profile.NAME]</c> tables (<c>--profile NAME</c>);
    /// <c>null</c> (the default) uses <see cref="Profile"/>.
    /// </summary>
    public string CustomProfileName { get; set; }

    /// <summary>
    /// Features to enable (<c>--features a,b</c>). Empty by default.
    /// </summary>
    public IList<string> Features { get; } = new List<string>();

    /// <summary>
    /// Enables every feature of the selected packages (<c>--all-features</c>).
    /// </summary>
    public bool AllFeatures { get; set; }

    /// <summary>
    /// Disables the <c>default</c> feature (<c>--no-default-features</c>).
    /// </summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>
    /// Workspace packages to act on (<c>-p NAME</c> each; not passed to fetch, which always
    /// fetches the whole workspace). Empty = cargo's default (the package in the folder, or the
    /// workspace's default members).
    /// </summary>
    public IList<string> Packages { get; } = new List<string>();

    /// <summary>
    /// Binary targets to build (<c>--bin NAME</c> each). Empty by default.
    /// </summary>
    public IList<string> Bins { get; } = new List<string>();

    /// <summary>
    /// Example targets to build (<c>--example NAME</c> each). Empty by default.
    /// </summary>
    public IList<string> Examples { get; } = new List<string>();

    /// <summary>
    /// Builds every target: lib, bins, examples, tests and benches (<c>--all-targets</c>; not
    /// passed to doc, clean, fmt or fetch).
    /// </summary>
    public bool AllTargets { get; set; }

    /// <summary>
    /// The target triple to compile for (<c>--target TRIPLE</c>); <c>null</c> (the default)
    /// builds for the host. The target must already be installed (<c>rustup target add</c>).
    /// </summary>
    public string TargetTriple { get; set; }

    /// <summary>
    /// For a macOS target built through a <see cref="MacOSLinkProfile"/> (on a Mac, or in a
    /// CodeBrix.RustTools.Docker container): the install name recorded in a cdylib, for example
    /// <c>@rpath/libmy_lib.dylib</c>. <c>null</c> (the default) keeps the linker's default, the
    /// output path, which makes the file depend on where it was built. Ignored for executables
    /// and for every other target. <see cref="MacOSLinkProfile.DefaultInstallName"/> computes the
    /// conventional value for a package with one cdylib.
    /// </summary>
    public string MacOSInstallName { get; set; }

    /// <summary>
    /// Requires Cargo.lock to be up to date (<c>--locked</c>).
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Never touches the network (<c>--offline</c>); every dependency must already be in the
    /// local registry cache - run <c>FetchAsync</c> (<see cref="CargoCommand.Fetch"/>) first to
    /// put it there.
    /// </summary>
    public bool Offline { get; set; }

    /// <summary>
    /// Equivalent to <see cref="Locked"/> plus <see cref="Offline"/> (<c>--frozen</c>).
    /// </summary>
    public bool Frozen { get; set; }

    /// <summary>
    /// The number of parallel jobs (<c>-j N</c>); <c>null</c> (the default) lets cargo decide.
    /// Negative values mean "CPU count minus N"; zero is rejected.
    /// </summary>
    public int? Jobs { get; set; }

    /// <summary>
    /// Arguments appended after all generated flags, unchanged (for example
    /// <c>--workspace</c>, or <c>--</c> followed by arguments for libtest or clippy).
    /// </summary>
    public IList<string> ExtraArguments { get; } = new List<string>();

    /// <summary>
    /// Environment variables for this command, applied after
    /// <see cref="RustWorkbenchOptions.Environment"/>; a <c>null</c> value removes a variable.
    /// </summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The maximum run time; <c>null</c> (the default) for no limit. On expiry the whole cargo
    /// process tree is killed and <see cref="CargoCommandResult.TimedOut"/> is set.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// The folder to run cargo in; <c>null</c> (the default) uses the project folder. When it
    /// differs from the project folder, <c>--manifest-path</c> is passed so the project is still
    /// the one acted on.
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Asks cargo for verbose output (<c>-v</c>): the full rustc command lines appear as
    /// <see cref="CargoProgressPhase.Running"/> progress.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Keeps every cargo JSON message line in <see cref="CargoCommandResult.RawMessages"/>.
    /// Off by default (the messages can be large).
    /// </summary>
    public bool KeepRawOutput { get; set; }

    /// <summary>
    /// The expected number of crates, reported as <see cref="CargoProgress.Total"/>. <c>null</c>
    /// (the default) lets <see cref="RustWorkbench"/> (and the Docker workbench) ESTIMATE it
    /// before a compiling command: the packages reachable from the selected packages over
    /// normal and build dependency edges in <c>cargo metadata --filter-platform</c> for the
    /// build's target (dev-dependencies of the selected packages too for <c>cargo test</c>,
    /// <see cref="AllTargets"/> and <see cref="Examples"/>). The metadata call is made only
    /// when someone receives the progress (a progress reporter is passed, or the workbench's
    /// progress event has a handler) or a <see cref="LicensePolicy"/> already needs that graph;
    /// otherwise the total stays unknown. A value set here is used as it is.
    /// <see cref="CargoRunner"/> on its own never estimates (no total unless set here).
    /// </summary>
    public int? ExpectedCrateCount { get; set; }

    /// <summary>
    /// When true (the default) cancellation kills the cargo process tree and throws
    /// <see cref="OperationCanceledException"/>; when false a result with
    /// <see cref="CargoCommandResult.WasCancelled"/> set is returned instead.
    /// </summary>
    public bool ThrowOnCancellation { get; set; } = true;

    /// <summary>
    /// Test only: runs every test binary even after one fails (<c>--no-fail-fast</c>).
    /// </summary>
    public bool NoFailFast { get; set; }

    /// <summary>
    /// Test only: runs only the tests whose name contains this text (cargo's positional
    /// <c>TESTNAME</c> argument); <c>null</c> (the default) runs every test.
    /// </summary>
    public string TestNameFilter { get; set; }

    /// <summary>
    /// Fmt only: when true (the default) only checks formatting (<c>--check</c>) and reports
    /// each unformatted region as a warning; when false rustfmt rewrites the files.
    /// </summary>
    public bool CheckOnly { get; set; } = true;

    /// <summary>
    /// The license gate. When set, <see cref="RustWorkbench"/> runs <c>cargo metadata</c> and
    /// audits every crate of the resolved graph against this policy BEFORE running a compiling
    /// command (build, check, test, doc, clippy); with the default
    /// <see cref="LicenseGateMode"/> (<see cref="Licensing.LicenseGateMode.Enforce"/>) cargo is
    /// not run when the audit fails. <c>null</c> (the default) means no gate.
    /// <see cref="CargoRunner"/> on its own ignores it.
    /// </summary>
    public LicensePolicy LicensePolicy { get; set; }

    /// <summary>
    /// When true (the default) a failed license gate throws
    /// <see cref="LicensePolicyViolationException"/>; when false the
    /// <see cref="RustWorkbench"/> returns a failed <see cref="CargoCommandResult"/> whose
    /// <see cref="CargoCommandResult.LicenseAudit"/> holds the report and whose
    /// <see cref="CargoCommandResult.LicenseGateFailed"/> is true. Ignored when
    /// <see cref="LicenseGateMode"/> is <see cref="Licensing.LicenseGateMode.ReportOnly"/>.
    /// </summary>
    public bool ThrowOnLicenseViolation { get; set; } = true;

    /// <summary>
    /// What a failed audit of the <see cref="LicensePolicy"/> gate does.
    /// <see cref="Licensing.LicenseGateMode.Enforce"/> (the default) stops the command before
    /// cargo runs (see <see cref="ThrowOnLicenseViolation"/>);
    /// <see cref="Licensing.LicenseGateMode.ReportOnly"/> audits AND builds in one call: the
    /// report is attached to <see cref="CargoCommandResult.LicenseAudit"/> whether it passed or
    /// not, cargo runs anyway, the result's success depends on cargo alone and
    /// <see cref="CargoCommandResult.LicenseGateFailed"/> stays false. Ignored when no
    /// <see cref="LicensePolicy"/> is set.
    /// </summary>
    public LicenseGateMode LicenseGateMode { get; set; } = LicenseGateMode.Enforce;

    /// <summary>
    /// Doc only: documents only the workspace packages, not their dependencies
    /// (<c>--no-deps</c>). Defaults to true.
    /// </summary>
    public bool NoDeps { get; set; } = true;

    /// <summary>
    /// Returns a copy of these options (lists and the environment are copied, the
    /// <see cref="LicensePolicy"/> object is shared).
    /// </summary>
    internal CargoBuildOptions Clone()
    {
        var copy = new CargoBuildOptions
        {
            Profile = Profile,
            CustomProfileName = CustomProfileName,
            AllFeatures = AllFeatures,
            NoDefaultFeatures = NoDefaultFeatures,
            AllTargets = AllTargets,
            TargetTriple = TargetTriple,
            MacOSInstallName = MacOSInstallName,
            Locked = Locked,
            Offline = Offline,
            Frozen = Frozen,
            Jobs = Jobs,
            Timeout = Timeout,
            WorkingDirectory = WorkingDirectory,
            Verbose = Verbose,
            KeepRawOutput = KeepRawOutput,
            ExpectedCrateCount = ExpectedCrateCount,
            ThrowOnCancellation = ThrowOnCancellation,
            NoFailFast = NoFailFast,
            TestNameFilter = TestNameFilter,
            CheckOnly = CheckOnly,
            LicensePolicy = LicensePolicy,
            ThrowOnLicenseViolation = ThrowOnLicenseViolation,
            LicenseGateMode = LicenseGateMode,
            NoDeps = NoDeps,
        };
        CopyInto(Features, copy.Features);
        CopyInto(Packages, copy.Packages);
        CopyInto(Bins, copy.Bins);
        CopyInto(Examples, copy.Examples);
        CopyInto(ExtraArguments, copy.ExtraArguments);
        foreach (var pair in Environment)
        {
            copy.Environment[pair.Key] = pair.Value;
        }

        return copy;
    }

    private static void CopyInto(IList<string> source, IList<string> destination)
    {
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
