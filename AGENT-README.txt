================================================================================
AGENT-README: CodeBrix.RustTools
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.RustTools.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.RustTools is a .NET 10 library for inspecting and building Rust
applications with the Rust toolchain that is ALREADY INSTALLED on the local
machine (Windows, macOS or Debian-based Linux). It runs the real tools -
rustup, cargo, rustc, clippy, rustfmt, rustdoc and the C compiler/linker -
and turns what they print into typed .NET objects.

It NEVER installs, updates or downloads a toolchain. When something is
missing or out of date it returns InstallGuidance objects (title,
explanation, ordered commands, documentation link) written for the host
operating system, for YOUR application to show to the user.

Feature areas:
  - Toolchain detection: every tool's status, path and parsed version; host
    triple; active / default / installed toolchains; installed targets and
    components; CARGO_HOME / RUSTUP_HOME; a project's rust-toolchain.toml.
  - Readiness ("can this machine build?") and per-OS install / update
    guidance.
  - Update check against the latest stable Rust and rustup releases, and
    read-only crates.io lookups.
  - Cargo projects: Cargo.toml (package, dependencies, features, workspace,
    targets, profiles), Cargo.lock, `cargo metadata`, project discovery.
  - License gate: SPDX license expressions evaluated against a policy for
    every crate in the resolved graph; a failed audit stops the build (or,
    with LicenseGateMode.ReportOnly, is reported while the build goes on).
  - Cargo commands (build, check, test, clean, doc, clippy, fmt, fetch) with
    typed diagnostics, artifacts, test counts, progress and cancellation
    that kills the whole cargo process tree. fetch downloads every
    dependency up front, so later commands can run offline.
  - Running the programs cargo built.
  - Source archives (zip, tar, tar.gz / tgz): safe extraction, then
    "extract and build" in one call.
  - Dependency health: tree listing, duplicate versions, outdated and yanked
    crates.
  - macOS: a link profile (rustup's rust-lld plus a libSystem symbol stub)
    that builds the Darwin targets without Apple's linker and SDK, on any
    host, and gives byte-for-byte the files a CodeBrix.RustTools.Docker
    container build gives; universal (fat) binaries joined from the arm64
    and x86_64 builds; Mach-O slice inspection.
  - Windows x64 and ARM64 without Microsoft tools: a link profile for Rust's
    windows-gnullvm targets with llvm-mingw, shared with the Docker package.
  - Linux of another architecture: a link profile for a GNU cross toolchain
    (Debian's gcc-aarch64-linux-gnu and friends), shared with the Docker
    package's Linux cross layers.
  - Android: finds the installed NDK, applies its clang and llvm-ar to a
    build as cargo-ndk would, builds both 64-bit ABIs and lays the .so files
    out for an APK (jniLibs) or a NuGet package (runtimes); guidance when
    the NDK or a target is missing.


INSTALLATION
============
PackageId:  CodeBrix.RustTools.MitLicenseForever

    dotnet add package CodeBrix.RustTools.MitLicenseForever

There is no package named plain "CodeBrix.RustTools"; that is the assembly
and root namespace name.

Dependencies (pulled in automatically; the package pins the version it was
built against):
  - CodeBrix.TomlParse.BsdLicenseForever   (reads Rust's TOML files;
                                            BSD-2-Clause)
Everything else (HTTP, JSON, processes, zip, tar) is the .NET base library.

License:       MIT
Requirements:  .NET 10 or later. To detect or build anything, Rust must be
               installed on the machine (normally with rustup); without it
               the library still works, reports every tool as Missing and
               returns guidance. Building also needs a C toolchain
               (linker); the workbench's compiling verbs refuse to start
               without one. Android builds need the Android NDK (found
               through ANDROID_NDK_HOME / ANDROID_HOME) and the Android
               targets' standard libraries (rustup target add); Google
               ships NDK host tools for Linux x86-64, macOS and Windows
               x86-64, so Linux and Windows ARM64 hosts cannot build for
               Android.
Network:       the library's own HTTP traffic is limited to
               CheckForToolUpdatesAsync / RustReleaseClient
               (static.rust-lang.org) and CratesIoClient /
               InspectDependenciesAsync(checkOutdated: true) (crates.io).
               cargo itself may download crates unless you set Offline or
               Frozen; FetchAsync (`cargo fetch`) downloads them all up
               front, so the commands after it can run with Offline.
               Detection is offline.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.RustTools;               // RustWorkbench,
                                            // RustWorkbenchOptions, exceptions
    using CodeBrix.RustTools.Toolchain;     // detection, readiness, guidance
    using CodeBrix.RustTools.Registry;      // release + crates.io clients
    using CodeBrix.RustTools.Projects;      // CargoProject, CargoManifest,
                                            // CargoLockFile, CargoMetadata
    using CodeBrix.RustTools.Licensing;     // LicensePolicy, LicenseAuditor,
                                            // SPDX expressions
    using CodeBrix.RustTools.Building;      // CargoRunner, CargoBuildOptions,
                                            // results, diagnostics, progress
    using CodeBrix.RustTools.Running;       // BuiltBinaryRunner, RunOptions
    using CodeBrix.RustTools.Archives;      // SourceArchiveExtractor,
                                            // ExtractAndBuildResult
    using CodeBrix.RustTools.Dependencies;  // DependencyInspector,
                                            // DependencyReport


CORE API REFERENCE
==================
Notation: "T Name" is a property; "= x" is its default; members are public.

RustWorkbench (sealed class, CodeBrix.RustTools) - the entry point
------------------------------------------------------------------
  RustWorkbench(RustWorkbenchOptions options = null)
  RustWorkbenchOptions Options { get; }
  event EventHandler<CargoProgress> Progress
      Every progress report of every cargo command this workbench runs
      (thread-pool thread; handler exceptions are ignored).

  Toolchain:
  Task<RustToolchainInfo> DetectToolchainAsync(
      string projectDirectory = null,
      CancellationToken cancellationToken = default)
      Pass the project folder so rustup applies its rust-toolchain.toml.
      Missing tools are reported, not thrown. DirectoryNotFoundException for
      a folder that does not exist. CACHED per workbench instance: the key
      is the project folder plus the path and content of the
      rust-toolchain.toml / rust-toolchain that applies to it (an edited
      file is detected afresh). Entries live for the workbench's lifetime
      unless Options.ToolchainCacheDuration is set. Thread-safe; detections
      run one at a time.
  Task<RustToolchainInfo> RefreshToolchainAsync(
      string projectDirectory = null,
      CancellationToken cancellationToken = default)
      Detects again, ignoring the cache, and caches the new result. Call it
      after installing or updating a tool.
  void InvalidateToolchainCache()
      Forgets every cached detection.
  ToolchainReadiness EvaluateReadiness(RustToolchainInfo info,
      ToolchainRequirements requirements = null)
      requirements null = cargo + rustc + C toolchain, any version.
  IReadOnlyList<InstallGuidance> GetInstallGuidance(
      ToolchainReadiness readiness)
      Instructions for every BLOCKING issue; empty when ready. Never runs
      anything.
  Task<ToolUpdateReport> CheckForToolUpdatesAsync(RustToolchainInfo info,
      CancellationToken cancellationToken = default)
      A download failure is recorded in report.Errors, not thrown.

  Projects and licenses:
  CargoProject OpenProject(string pathToCargoTomlOrDirectory)
      Reads Cargo.toml + Cargo.lock; nothing runs. FileNotFoundException
      when there is no Cargo.toml; RustToolsException for invalid TOML.
  Task<LicenseAuditReport> AuditLicensesAsync(CargoProject project,
      LicensePolicy policy, CancellationToken cancellationToken = default)
  Task<LicenseAuditReport> AuditLicensesAsync(CargoProject project,
      LicensePolicy policy, CargoMetadataOptions metadataOptions,
      CancellationToken cancellationToken = default)
      Runs `cargo metadata`, audits every crate, returns the report whether
      it passed or not.

  Cargo commands (all return Task<CargoCommandResult> unless noted):
  RunCargoAsync(CargoProject project, CargoCommand command,
      CargoBuildOptions options = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
  BuildAsync(CargoProject project, CargoBuildOptions options = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
  CheckAsync(...same parameters...)
  Task<CargoTestResult> TestAsync(...same parameters...)
  CleanAsync(...same parameters...)
  DocAsync(...same parameters...)
  ClippyAsync(...same parameters...)
  FmtAsync(...same parameters...)
  FetchAsync(...same parameters...)

      For build, check, test, doc and clippy the workbench, in order:
        1. detects the toolchain from the project folder (from the cache
           when valid) and evaluates ToolchainRequirements.Default; when
           cargo, rustc or the C compiler / linker (RustTool.CCompiler) is
           Missing it throws ToolchainNotFoundException carrying Readiness
           and Guidance (for the C toolchain: the Visual Studio Build Tools
           C++ workload on Windows, `xcode-select --install` on macOS,
           `sudo apt install build-essential pkg-config` on Debian) - cargo
           is never started. A C toolchain whose status is Unknown does NOT
           block: it is a non-blocking ToolStatusUnknown issue in
           result.Readiness and cargo runs;
           When options.TargetTriple is a Linux target (LinuxLinkProfile
           .IsLinuxTriple) other than the host's own triple, step 1 also
           requires that target's standard library (rustup-managed
           toolchains only) and - when options.Environment (or
           RustWorkbenchOptions.Environment) carries a
           CARGO_TARGET_<TRIPLE>_LINKER of the <prefix>-gcc form, as
           LinuxLinkProfile.ApplyTo sets it - the profile's cross gcc and
           ar; a missing one throws ToolchainNotFoundException with
           InstallLinuxCrossTools guidance (`rustup target add <triple>`
           plus the Debian packages) instead of cargo failing with "can't
           find crate for `std`" (exit 101) or a linker error;
        2. when options.LicensePolicy is set, OR options.ExpectedCrateCount
           is null AND a progress receiver exists (a progress argument, or
           a handler on the workbench's Progress event), runs ONE
           `cargo metadata --filter-platform <triple>`
           (TargetTriple, else the host triple; no filter for a .json
           target spec) with the options' Offline / Locked / Frozen /
           Features / AllFeatures / NoDefaultFeatures. Without a policy a
           failed `cargo metadata` is not an error: the total is just
           unknown (null). With no policy and no progress receiver no
           metadata call is made (nobody would see the total);
        3. when a policy is set, audits that graph. On failure with
           LicenseGateMode.Enforce (the default) it throws
           LicensePolicyViolationException (ThrowOnLicenseViolation = true,
           the default) or returns a failed result with LicenseGateFailed
           true and LicenseAudit set - cargo is NOT run either way. With
           LicenseGateMode.ReportOnly it reports a CargoProgressPhase.Warning
           progress "License audit failed (report only): N rejected, M
           unresolved", ignores ThrowOnLicenseViolation and goes on - one
           call audits AND builds;
        4. when ExpectedCrateCount is null and step 2 read the graph,
           ESTIMATES CargoProgress.Total from it: the distinct packages
           reachable from the selected packages (options.Packages, else
           --workspace / --all in ExtraArguments = every member, else the
           manifest's package when it is a member below the workspace
           root, else the default members)
           over normal and build dependency edges. Dev-dependencies count
           only for the selected packages and only for test, AllTargets,
           Examples or --tests/--benches/--examples/--all-targets. A value
           you set is used as it is;
        5. runs cargo in project.Directory and attaches LicenseAudit (passed
           or not, in ReportOnly) and Readiness.
      clean and fmt skip steps 1-4. fetch checks only that cargo and rustc
      are installed (downloading needs no C toolchain) and skips steps 2-4
      and the Linux cross check. The caller's options object is never
      modified. A failed compile is a result with Success false, not an
      exception.

      FetchAsync runs `cargo fetch`: it downloads every dependency of the
      project into the local cache (CARGO_HOME) - for every platform, or
      with options.TargetTriple for that target only - and compiles
      nothing. Afterwards every command, the license gate's `cargo
      metadata` and InspectDependenciesAsync included, can run with
      Offline = true. Of the options only TargetTriple, Locked, Offline,
      Frozen, Verbose, Environment, Timeout and ExtraArguments apply; no
      license gate, no crate-count estimate. Each download is a
      CargoProgressPhase.Downloading report naming the crate (none for
      crates already cached). A failed download (for example no network)
      is a result with Success false and cargo's message in Errors and
      StandardError, not an exception; a missing cargo or rustc throws
      ToolchainNotFoundException with guidance.

  Dependencies, archives, running:
  Task<DependencyReport> InspectDependenciesAsync(CargoProject project,
      bool checkOutdated = false, CargoMetadataOptions metadataOptions = null,
      CancellationToken cancellationToken = default)
  Task<ExtractAndBuildResult> ExtractAndBuildAsync(string archivePath,
      string destinationDirectory = null, CargoBuildOptions options = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
      Extracts (null destination = new temp folder, deleted on Dispose),
      opens the workspace root (else the shallowest package), then
      BuildAsync. No Cargo.toml -> SourceArchiveException. Whenever it
      throws after extracting, the extraction is disposed first. Uses the
      default SourceArchiveOptions.
  Task<ExtractAndBuildResult> ExtractAndBuildAsync(string archivePath,
      string destinationDirectory, SourceArchiveOptions archiveOptions,
      CargoBuildOptions options = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
      The same, with extraction limits and settings (MaxEntryCount,
      MaxTotalBytes, StripSingleRootFolder, Overwrite, DeleteOnDispose);
      null archiveOptions = the defaults. A limit exceeded ->
      SourceArchiveException before anything is built.
  Task<RunResult> RunAsync(BuildArtifact artifact, RunOptions options = null,
      CancellationToken cancellationToken = default)

  Android:
  Task<AndroidBuildResult> BuildAndroidAsync(CargoProject project,
      string outputDirectory, CargoBuildOptions options = null,
      AndroidBuildOptions androidOptions = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
      Builds aarch64-linux-android, then x86_64-linux-android (or the
      triples in androidOptions), each through RunCargoAsync with
      AndroidLinkProfile applied (the NDK from detection at
      androidOptions.ApiLevel, default 24, unless androidOptions.Profile
      is set), and copies every successful build's .so files (and
      programs when IncludeExecutables) into outputDirectory in the
      chosen AndroidOutputLayout (jniLibs/<abi>/ by default). Throws
      ToolchainNotFoundException - guidance included - when a target's
      standard library or the NDK is missing, or this machine cannot run
      the NDK. Crates that compile C work: the NDK is the C toolchain.

  Linux, another architecture (host-side GNU cross toolchain):
  Task<LinuxBuildResult> BuildLinuxAsync(CargoProject project,
      string outputDirectory, CargoBuildOptions options = null,
      LinuxBuildOptions linuxOptions = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
      Builds every Linux target other than the host's own triple (on an
      x86-64 Linux host: aarch64-unknown-linux-gnu, then
      riscv64gc-unknown-linux-gnu; or the triples in linuxOptions), each
      through RunCargoAsync with LinuxLinkProfile applied (ForTriple per
      target with the tools on PATH or in linuxOptions.ToolDirectory,
      unless linuxOptions.Profile is set), and copies every successful
      build's .so files (and programs when IncludeExecutables) into
      outputDirectory in the chosen LinuxOutputLayout
      (runtimes/<rid>/native/ by default). BEFORE any cargo runs it
      evaluates ToolchainReadiness for each target's standard library and
      each profile's cross gcc and ar, and throws
      ToolchainNotFoundException - guidance included: `rustup target add`
      and `sudo apt install gcc-<arch>-linux-gnu g++-<arch>-linux-gnu
      libc6-dev-<arch>-cross` - when one is missing. The host's own triple
      is rejected (ArgumentException): build it natively with BuildAsync.

  macOS:
  Task<MacOSUniversalBuildResult> BuildMacOSUniversalAsync(
      CargoProject project, string outputDirectory,
      CargoBuildOptions options = null, MacOSLinkProfile profile = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
      Builds aarch64-apple-darwin, then x86_64-apple-darwin (both targets
      must be installed - ToolchainNotFoundException with the `rustup
      target add` guidance otherwise), each through RunCargoAsync, and
      joins every program and dynamic library both produced into a
      universal file in outputDirectory (MacOSUniversalBinary). Without a
      profile the builds use the machine's own linker and SDK (on a Mac:
      Apple's); with one, rustup's rust-lld and the profile's stub, which
      is what matches a container build. With a profile,
      options.MacOSInstallName defaults to
      MacOSLinkProfile.DefaultInstallName(project).

RustWorkbenchOptions (sealed class) - settings shared by every component
------------------------------------------------------------------------
  string CargoPath, RustcPath, RustupPath   explicit executables (null =
                                            search); an invalid path is
                                            reported as Missing, no fallback
  string AndroidNdkPath an NDK root to use instead of searching; a folder
                        that is not an NDK for this machine = no NDK, no
                        fallback
  string CargoHome      searched for bin/, passed to tools as CARGO_HOME
  string RustupHome     passed to tools as RUSTUP_HOME
  IList<string> AdditionalSearchDirectories   searched before PATH
  IDictionary<string,string> Environment      added to every process; a null
                                              value removes the variable
  TimeSpan ToolProbeTimeout = 60 s            per detection probe
  TimeSpan? ToolchainCacheDuration = null     how long a workbench serves a
                                              cached detection; null = for
                                              its lifetime, TimeSpan.Zero =
                                              never cache; negative throws
                                              ArgumentOutOfRangeException
  HttpClient HttpClient                       used as-is, never disposed
  HttpMessageHandler HttpMessageHandler       used when HttpClient is null
  string UserAgent                            null = DefaultUserAgent
  const string DefaultUserAgent
      "CodeBrix.RustTools (https://github.com/ellisnet/CodeBrix.RustTools)"
  string EffectiveUserAgent { get; }
  Uri RustDistributionBaseUri = https://static.rust-lang.org/
  Uri CratesIoApiBaseUri = https://crates.io/api/v1/
  TimeSpan CratesIoRequestInterval = 1 s      process-wide spacing;
                                              TimeSpan.Zero disables

Executable search order: the explicit path option (cargo/rustc/rustup only),
AdditionalSearchDirectories, PATH, CARGO_HOME/bin, then ~/.cargo/bin
(%USERPROFILE%\.cargo\bin on Windows; ".exe" is added on Windows).

Error model (namespace CodeBrix.RustTools)
------------------------------------------
  RustToolsException                    base of every library exception
    ToolchainNotFoundException          ToolchainReadiness Readiness (may be
                                        null), IReadOnlyList<InstallGuidance>
                                        Guidance (never null)
    CargoCommandException               int? ExitCode, string StandardError,
                                        IReadOnlyList<InstallGuidance> Guidance
    LicensePolicyViolationException     string Summary (multi-line, never
                                        null), LicenseAuditReport Report
                                        (null when created without one)
    SourceArchiveException              string ArchivePath
  Detection and update checks report problems as data (ToolStatus,
  Warnings, Errors); a failed build is a result, not an exception.

--- Toolchain (CodeBrix.RustTools.Toolchain) ---

RustToolchainInfo (sealed class, init-only snapshot)
  OperatingSystemKind OperatingSystem; string OperatingSystemDescription,
  Architecture; IReadOnlyDictionary<RustTool, InstalledTool> Tools;
  InstalledTool Cargo, Rustc, Rustup, Clippy, Rustfmt, Rustdoc, CCompiler,
  Linker; InstalledTool GetTool(RustTool tool); string HostTriple,
  LlvmVersion, Sysroot (from `rustc --print sysroot`: the folder with the
  precompiled standard libraries and rustup's own tools such as rust-lld),
  RustcCommitHash, ActiveToolchain, ActiveToolchainReason,
  DefaultToolchain; RustChannel Channel; IReadOnlyList<string>
  InstalledToolchains, InstalledTargets, InstalledComponents; AndroidNdk
  AndroidNdk (null when none was found or the machine cannot run one);
  string CargoHome, RustupHome, ProjectDirectory; RustToolchainFile ToolchainFile
  (null when none applies); DateTimeOffset DetectedAt; IReadOnlyList<string>
  Warnings; bool IsRustupManaged, HasCargoAndRustc; ToString() = multi-line.

InstalledTool (sealed class)
  RustTool Tool; ToolStatus Status; string ExecutablePath; ToolVersion
  Version; string RawVersionOutput, Message; bool IsAvailable
  static InstalledTool Missing(RustTool tool, string message)
  static InstalledTool Unknown(RustTool tool, string message,
                               string executablePath = null)

RustTool:            Rustup, Cargo, Rustc, Clippy, Rustfmt, Rustdoc,
                     CCompiler, Linker
ToolStatus:          Installed, Missing, Outdated, Unknown
RustChannel:         Unknown, Stable, Beta, Nightly, Dev
OperatingSystemKind: Unknown, Windows, MacOS, DebianLinux, OtherLinux

ToolVersion (sealed record, IComparable<ToolVersion>)
  Parses tool output such as "cargo X.Y.Z (hash date)",
  "rustc X.Y.Z-nightly (hash date)", "rustfmt X.Y.Z-stable (hash date)" or a
  bare "X.Y.Z".
  ToolVersion(int major, int minor, int patch, string preRelease = null)
  static ToolVersion Parse(string text)       FormatException if none
  static bool TryParse(string text, out ToolVersion version)
  int Major, Minor, Patch; string PreRelease; RustChannel Channel; string
  ToolName, CommitHash, Raw; DateOnly? CommitDate
  bool IsAtLeast(int major, int minor, int patch = 0)
  int CompareTo(ToolVersion other); operators < > <= >=
  Equality and ordering use ONLY the numbers and the pre-release label.

RustToolchainDetector (sealed class)
  RustToolchainDetector(RustWorkbenchOptions options = null)
  Task<RustToolchainInfo> DetectAsync(string projectDirectory = null,
      CancellationToken cancellationToken = default)

RustToolchainFile (sealed class)
  const string FileName = "rust-toolchain.toml",
               LegacyFileName = "rust-toolchain"
  string FilePath, Channel, Profile, ToolchainPath; bool IsLegacyFormat;
  IReadOnlyList<string> Components, Targets; RustChannel ChannelKind
  static RustToolchainFile Parse(string text, string filePath = null)
  static RustToolchainFile Load(string filePath)
  static string Find(string directory)                 walks up
  static RustToolchainFile FindAndLoad(string directory)   null if none
  static RustChannel ChannelOf(string channelOrToolchainName)

HostOperatingSystem (static class)
  OperatingSystemKind Detect()   Debian family = /etc/os-release ID or
                                 ID_LIKE contains "debian"
  string Describe()
  OperatingSystemKind ClassifyOsRelease(string osReleaseText)
  IReadOnlyDictionary<string,string> ParseOsRelease(string osReleaseText)

ToolchainRequirements (sealed class)
  ToolVersion MinimumRustVersion (null = any); bool RequireCCompiler = true,
  RequireClippy, RequireRustfmt, RequireRustup;
  bool RequireToolchainFileChannel = true; IList<string> RequiredTargets;
  bool RequireAndroidNdk (an NDK must have been found);
  IDictionary<string, LinuxLinkProfile> RequiredLinuxCrossTools (by
  triple: the profile's CompilerPath and ArchiverPath must exist on PATH
  or in its BinDirectory)
  static ToolchainRequirements Default { get; }
  static ToolchainRequirements WithLintAndFormat()
  static ToolchainRequirements ForAndroid()   both 64-bit Android targets
                                              + the NDK
  static ToolchainRequirements ForLinuxCross(IEnumerable<string> triples,
      string binDirectory = null)   each target + its cross gcc and ar

ToolchainReadiness (sealed class)
  static ToolchainReadiness Evaluate(RustToolchainInfo toolchain,
                                     ToolchainRequirements requirements = null)
  bool IsReady; IReadOnlyList<ReadinessIssue> Issues (blocking first);
  IReadOnlyList<RustTool> MissingTools, OutdatedTools; IReadOnlyList<string>
  MissingTargets, MissingLinuxCrossTargets; bool AndroidNdkMissing;
  RustToolchainInfo Toolchain;
  ToolchainRequirements Requirements; OperatingSystemKind OperatingSystem

ReadinessIssue: ReadinessIssueKind Kind; RustTool? Tool; string Subject,
  Message; bool IsBlocking
ReadinessIssueKind: ToolMissing, ToolOutdated, ToolStatusUnknown,
  TargetMissing, ToolchainNotInstalled, AndroidNdkMissing,
  LinuxCrossToolMissing (Subject = the triple)
  (a required tool whose status is Unknown - could not be determined - is
  a ToolStatusUnknown warning, never a blocker; this includes the C
  toolchain)

InstallGuidance (sealed class)
  InstallGuidanceTopic Topic; OperatingSystemKind OperatingSystem; string
  Title, Explanation, DocumentationUrl; IReadOnlyList<string> Commands (in
  order; lines starting with '#' are comments); bool RequiresAdministrator,
  RequiresNewShell; ToString() = printable block
InstallGuidanceTopic: InstallRust, UpdateRust, InstallRustup,
  InstallComponents, InstallCToolchain, AddTargets, InstallToolchain,
  InstallAndroidNdk, InstallLinuxCrossTools (items = Linux triples:
  `rustup target add` + the Debian cross packages)
InstallGuidanceProvider (static class)
  IReadOnlyList<InstallGuidance> ForReadiness(ToolchainReadiness readiness)
  InstallGuidance ForUpdate(OperatingSystemKind operatingSystem,
                            bool rustupManaged)
  InstallGuidance GetGuidance(InstallGuidanceTopic topic,
      OperatingSystemKind operatingSystem, IEnumerable<string> items = null,
      bool rustupManaged = true)
  const string RustInstallUrl
  What the guidance says: Rust via rustup (winget on Windows, the official
  script elsewhere); C toolchain = Visual Studio Build Tools (C++ workload)
  on Windows, xcode-select --install on macOS, sudo apt install
  build-essential pkg-config on Debian; update = rustup update; components
  = rustup component add clippy rustfmt; Android NDK = Android Studio's
  SDK Manager or sdkmanager --install "ndk;<version>", then
  ANDROID_NDK_HOME (RequiresNewShell).

AndroidNdk (sealed class) - an installed Android NDK
  string Path, Version (Pkg.Revision from source.properties, or null),
  HostTag (linux-x86_64 / darwin-x86_64 / windows-x86_64),
  ToolchainDirectory, ToolchainBinDirectory, SysrootDirectory,
  ArchiverPath (llvm-ar), RanlibPath; IReadOnlyList<int> ApiLevels (from
  the clang wrappers present); bool WindowsFileNames (.cmd / .exe)
  string ClangWrapperPath(string triple, int apiLevel)
  string ClangPlusPlusWrapperPath(string triple, int apiLevel)
  bool SupportsApiLevel(int apiLevel)
  static AndroidNdk Load(string directory, string hostTag = null)
      DirectoryNotFoundException when the folder has no
      toolchains/llvm/prebuilt/<hostTag>/bin
  static bool TryLoad(string directory, string hostTag, out AndroidNdk ndk)
AndroidNdkLocator (static class) - finding the NDK, never installing it
  static string HostTag          this machine's, or null when Google ships
                                 no NDK host tools for it
  static string HostSupportMessage   why HostTag is null, else null
  static string HostTagFor(OperatingSystemKind os, Architecture arch)
      Linux and Windows need X64; macOS takes X64 and Arm64 (darwin-x86_64
      holds universal binaries)
  static AndroidNdk Find(string explicitPath = null)
      explicitPath (no fallback), else ANDROID_NDK_HOME, ANDROID_NDK_ROOT,
      then the newest ndk/<version> under ANDROID_HOME or ANDROID_SDK_ROOT,
      then <sdk>/ndk-bundle; null when nothing is found
  static AndroidNdk Find(string explicitPath,
      Func<string,string> getEnvironmentVariable, string hostTag)
  static AndroidNdk FindInSdk(string sdkDirectory, string hostTag = null)
  static IReadOnlyList<string> ListSideBySideNdks(string sdkDirectory)
      newest version first (numeric, part by part)
  const string NdkHomeVariable, NdkRootVariable, SdkHomeVariable,
               SdkRootVariable

--- Registry (CodeBrix.RustTools.Registry) ---

RustReleaseClient (sealed class)
  RustReleaseClient(RustWorkbenchOptions options = null)
  Task<RustReleaseInfo> GetLatestStableReleaseAsync(
      CancellationToken cancellationToken = default)
  Task<RustReleaseInfo> GetChannelReleaseAsync(RustChannel channel,
      CancellationToken cancellationToken = default)
  Task<ToolVersion> GetLatestRustupVersionAsync(
      CancellationToken cancellationToken = default)
RustReleaseInfo: string Channel; DateOnly? Date; ToolVersion RustcVersion,
  CargoVersion (= RustcVersion, what `cargo --version` prints),
  CargoPackageVersion (cargo's manifest number), ClippyVersion,
  RustfmtVersion

CratesIoClient (sealed class)
  CratesIoClient(RustWorkbenchOptions options = null)
  Task<CrateInfo> GetCrateAsync(string crateName,
      CancellationToken cancellationToken = default)
      null when the crate does not exist; ArgumentException for an invalid
      name; RustToolsException for other failures.
  Task<string> GetLatestVersionAsync(string crateName,
      CancellationToken cancellationToken = default)
CrateInfo: string Name, Description, Repository, Homepage, Documentation,
  MaxStableVersion, NewestVersion, MaxVersion, LatestVersion; long
  Downloads; DateTimeOffset? UpdatedAt; IReadOnlyList<CrateVersionInfo>
  Versions; CrateVersionInfo GetVersion(string number);
  bool? IsYanked(string number)
CrateVersionInfo: string Number; ToolVersion Version; bool IsYanked; string
  License; DateTimeOffset? CreatedAt; string RustVersion; bool IsPreRelease

ToolUpdateReport (sealed class)
  IReadOnlyList<ToolUpdateCheck> Checks; RustReleaseInfo LatestRelease;
  ToolVersion LatestRustupVersion; IReadOnlyList<InstallGuidance> Guidance;
  IReadOnlyList<string> Errors; DateTimeOffset CheckedAt; bool
  AnyUpdateAvailable; IReadOnlyList<ToolUpdateCheck> OutdatedTools;
  ToolUpdateCheck GetCheck(RustTool tool)
  static ToolUpdateReport Create(RustToolchainInfo toolchain,
      RustReleaseInfo latestRelease, ToolVersion latestRustup,
      IEnumerable<string> errors = null)                    (no network)
ToolUpdateCheck: RustTool Tool; ToolVersion Installed, Latest; ToolStatus
  Status; bool IsUpdateAvailable

--- Projects (CodeBrix.RustTools.Projects) ---

CargoProject (sealed class) - a project on disk
  static CargoProject Open(string pathToCargoTomlOrDirectory,
                           RustWorkbenchOptions options = null)
  string ManifestPath, Directory; CargoManifest Manifest; CargoLockFile
  LockFile (a member uses its workspace root's lock; null when none);
  RustWorkbenchOptions Options; bool IsWorkspaceRoot, IsVirtual; string
  WorkspaceRootManifestPath (null for a standalone package); CargoManifest
  WorkspaceRootManifest; string Name (null for a virtual manifest);
  CargoPackage EffectivePackage (workspace inheritance applied);
  IReadOnlyList<string> WorkspaceMemberDirectories
  IReadOnlyList<CargoProject> GetWorkspaceMembers()
  IReadOnlyList<CargoTargetDeclaration> GetDeclaredTargets()
      declared + conventional targets (src/lib.rs, src/main.rs, src/bin,
      examples, tests, benches, build.rs), no cargo run
  Task<CargoMetadata> GetMetadataAsync(CargoMetadataOptions options = null,
      CancellationToken cancellationToken = default)
      ToolchainNotFoundException / CargoCommandException on failure.

CargoMetadataOptions (sealed class)
  bool Offline, Locked, Frozen, NoDependencies, AllFeatures,
  NoDefaultFeatures; string FilterPlatform; IList<string> Features,
  ExtraArguments; TimeSpan? Timeout (null = 5 minutes)

CargoManifest (sealed class) - a parsed Cargo.toml
  const string FileName = "Cargo.toml"
  static CargoManifest Load(string path)
  static CargoManifest Parse(string text, string filePath = null)
  string FilePath, Directory; CargoPackage Package; CargoWorkspace Workspace;
  IReadOnlyList<CargoDependency> Dependencies (all kinds and targets);
  IReadOnlyList<CargoFeature> Features; CargoTargetDeclaration Lib;
  IReadOnlyList<CargoTargetDeclaration> Bins, Examples, Tests, Benches;
  IReadOnlyList<CargoProfileDeclaration> Profiles; bool IsWorkspaceRoot,
  IsVirtual
  IReadOnlyList<CargoDependency> GetDependencies(CargoDependencyKind kind)
  CargoProfileDeclaration GetProfile(string name)
  CargoFeature GetFeature(string name)

CargoPackage: string Name, Version, Edition, Description, License,
  LicenseFile, Repository, Homepage, Documentation, Readme, RustVersion,
  BuildScript, Links, DefaultRun; IReadOnlyList<string> Authors, Keywords,
  Categories, InheritedFields; bool? Publish, AutoBins, AutoExamples,
  AutoTests, AutoBenches; bool BuildScriptDisabled;
  bool IsInherited(string field);
  CargoPackage WithInheritance(CargoPackage workspacePackage)
CargoDependency: string Name, PackageName, VersionRequirement, Path, Git,
  Branch, Tag, Rev, Registry, Target (cfg); IReadOnlyList<string> Features;
  bool Optional, DefaultFeatures = true, IsWorkspaceInherited, IsPath,
  IsGit, IsRegistry; CargoDependencyKind Kind
CargoDependencyKind: Normal, Dev, Build
CargoFeature: string Name; IReadOnlyList<string> Enables; bool IsDefault
CargoWorkspace: IReadOnlyList<string> Members, DefaultMembers, Exclude;
  string Resolver; CargoPackage Package ([workspace.package]);
  IReadOnlyList<CargoDependency> Dependencies
CargoTargetDeclaration: string Name, Path; CargoTargetKind Kind; bool
  IsDeclared, Harness = true, IsProcMacro; bool? Test, Bench;
  IReadOnlyList<string> RequiredFeatures, CrateTypes
CargoProfileDeclaration: string Name, OptLevel, Inherits, Lto, Debug; long?
  CodegenUnits; IReadOnlyList<string> PackageOverrides
CargoTargetKind: Unknown, Bin, Lib, Rlib, Dylib, Cdylib, Staticlib,
  ProcMacro, Example, Test, Bench, CustomBuild
CargoTargetKinds (static): CargoTargetKind Parse(string text);
  string ToCargoString(CargoTargetKind kind); bool IsLibrary(CargoTargetKind)

CargoLockFile (sealed class)
  const string FileName = "Cargo.lock"
  static CargoLockFile Load(string path)
  static CargoLockFile Parse(string text, string filePath = null)
  string FilePath; int? FormatVersion; IReadOnlyList<CargoLockPackage>
  Packages; IReadOnlyList<CargoLockPackage> Find(string name)
CargoLockPackage: string Name, Version, Source, Checksum;
  IReadOnlyList<string> Dependencies; bool IsRegistry, IsGit, IsLocal;
  IReadOnlyList<string> GetDependencyNames()

CargoMetadata (sealed class) - parsed `cargo metadata --format-version 1`
  const string CratesIoSource, CratesIoSparseSource
  static CargoMetadata Parse(string json)
  IReadOnlyList<CargoMetadataPackage> Packages; IReadOnlyList<string>
  WorkspaceMembers, WorkspaceDefaultMembers; IReadOnlyList<CargoResolveNode>
  ResolveNodes; string ResolveRoot, WorkspaceRoot, TargetDirectory; int
  FormatVersion; bool HasResolve
  CargoMetadataPackage GetPackage(string id)
  IReadOnlyList<CargoMetadataPackage> FindPackages(string name)
  bool IsWorkspaceMember(string id)
  IReadOnlyList<CargoMetadataPackage> GetWorkspacePackages()
  CargoResolveNode GetNode(string id)
  IReadOnlyList<string> GetBuildClosure(IEnumerable<string> rootPackageIds,
      bool includeRootDevDependencies = false)
      The package ids a build of the roots compiles: the roots plus every
      package reachable over normal and build edges (dep_kinds; an edge
      without dep_kinds counts as normal), each once, roots first. Dev
      edges are followed only from the roots and only when asked. Use
      metadata from --filter-platform (CargoMetadataOptions.FilterPlatform)
      so other platforms' dependencies are not in the graph. This is what
      the workbench counts for CargoProgress.Total.
CargoMetadataPackage: string Id, Name, Version, License, LicenseFile,
  ManifestPath, Source, Description, Repository, Edition, RustVersion,
  ManifestDirectory, LicenseFilePath; IReadOnlyList<CargoMetadataTarget>
  Targets; IReadOnlyList<CargoMetadataDependency> Dependencies;
  IReadOnlyDictionary<string, IReadOnlyList<string>> Features; bool
  IsLocal, IsRegistry, IsCratesIo, IsGit
CargoMetadataTarget: string Name, SrcPath, Edition; IReadOnlyList<
  CargoTargetKind> Kinds, CrateTypes; IReadOnlyList<string>
  RequiredFeatures; CargoTargetKind Kind; bool HasKind(CargoTargetKind kind)
CargoMetadataDependency: string Name, Requirement, Target, Rename, Source,
  Path; CargoDependencyKind Kind; bool Optional, UsesDefaultFeatures;
  IReadOnlyList<string> Features
CargoResolveNode: string Id; IReadOnlyList<CargoResolveDependency> Deps;
  IReadOnlyList<string> Features
CargoResolveDependency: string Name, PackageId; IReadOnlyList<
  CargoResolveDependencyKind> DepKinds; bool HasKind(CargoDependencyKind
  kind), IsDevOnly
CargoResolveDependencyKind: CargoDependencyKind Kind; string Target

CargoProjectLocator (static class) - finding projects in a folder tree
  const int DefaultMaxDepth = 6
  IReadOnlyList<string> FindManifests(string rootDirectory,
      int maxDepth = DefaultMaxDepth)
      every Cargo.toml; skips target, node_modules and hidden folders
  IReadOnlyList<string> FindProjectRoots(string rootDirectory,
      int maxDepth = DefaultMaxDepth, bool includeNested = false)
      outermost manifests only (members and vendored crates excluded)
  string FindRoot(string path)        owning manifest, preferring the
                                      workspace root
  string FindNearestManifest(string directory)        at or above
  string FindWorkspaceRootManifest(string packageDirectory)
  IReadOnlyList<string> ExpandWorkspaceMembers(string workspaceDirectory,
      CargoWorkspace workspace)

--- Licensing (CodeBrix.RustTools.Licensing) ---

LicensePolicy (sealed class)
  ISet<OpenSourceLicense> AllowedLicenses; ISet<string> AllowedExceptions
  (case-insensitive); UnresolvedLicenseHandling UnresolvedLicenseHandling =
  Reject; bool ExemptWorkspaceMembers = true; IDictionary<string,string>
  CrateOverrides (crate name -> reason, case-insensitive)
  static LicensePolicy Permissive()     MIT, MIT-0, Apache-2.0,
      BSD-2-Clause, BSD-3-Clause, 0BSD, ISC, Zlib, BSL-1.0, Unicode-3.0,
      Unicode-DFS-2016, Unlicense, CC0-1.0
  static LicensePolicy PermissiveAndWeakCopyleft()   + MPL-2.0 and every
      LGPL version
  LicensePolicy Allow(params OpenSourceLicense[] licenses)       fluent
  LicensePolicy AllowException(params string[] exceptionIds)     fluent
  LicensePolicy Override(string crateName, string reason)        fluent
  bool IsAllowed(OpenSourceLicense license)   (an -or-later GNU license is
      allowed when a later version it can be used under is allowed)
  bool IsExceptionAllowed(SpdxLicenseException exception)
  LicensePolicy Clone()
UnresolvedLicenseHandling: Reject (audit fails), Warn (passes, listed in
  report.Warnings), Allow (passes silently)

LicenseAuditor (static class)
  Task<LicenseAuditReport> AuditAsync(CargoProject project,
      LicensePolicy policy, CancellationToken cancellationToken = default)
  Task<LicenseAuditReport> AuditAsync(CargoProject project,
      LicensePolicy policy, CargoMetadataOptions metadataOptions,
      CancellationToken cancellationToken = default)
      ArgumentException when metadataOptions.NoDependencies is set.
  LicenseAuditReport Audit(CargoMetadata metadata, LicensePolicy policy)
      no process is run
  CrateLicenseFinding Evaluate(CargoMetadataPackage package,
      bool isWorkspaceMember, LicensePolicy policy)

LicenseAuditReport (sealed class)
  IReadOnlyList<CrateLicenseFinding> Findings (sorted by name, version);
  LicensePolicy Policy; string WorkspaceRoot; bool Passed; int
  ApprovedCount, RejectedCount, UnresolvedCount, ExemptCount;
  IReadOnlyList<CrateLicenseFinding> BlockingFindings, Rejected, Unresolved;
  IReadOnlyList<string> Warnings
  CrateLicenseFinding GetFinding(string crateName)
  string ToSummaryText()        counts, then each blocking crate, then warnings
  void ThrowIfFailed()          LicensePolicyViolationException
CrateLicenseFinding: string CrateName, Version, DeclaredExpression,
  LicenseFile, Reason, Source, ManifestPath, PackageId;
  SpdxLicenseExpression ParsedExpression; CrateLicenseStatus Status; bool
  IsWorkspaceMember, IsBlocking
CrateLicenseStatus: Approved, Rejected, Unresolved, Exempt
  Unresolved = only a license-file, no license at all, an unknown SPDX id
  or an unparsable expression.

SpdxLicenseExpression (sealed class) - SPDX expression parser/evaluator
  static SpdxLicenseExpression Parse(string text)      FormatException
  static bool TryParse(string text, out SpdxLicenseExpression expression)
  static bool TryParse(string text, out SpdxLicenseExpression expression,
                       out string error)
      identifiers, "+", WITH, AND, OR, parentheses, legacy "/" (= OR)
  SpdxExpressionKind Kind (License, And, Or); string Identifier;
  OpenSourceLicense? License; bool IsOrLater, IsKnownLicense;
  SpdxLicenseException Exception; IReadOnlyList<SpdxLicenseExpression>
  Operands; IReadOnlyList<string> UnknownIdentifiers;
  IReadOnlyList<OpenSourceLicense> Licenses
  LicenseEvaluation Evaluate(LicensePolicy policy)  OR = any operand,
                                                    AND = every operand
  ToString()                                        canonical text
LicenseEvaluation: LicenseEvaluationOutcome Outcome (Approved, Rejected,
  Unresolved); string Reason; IReadOnlyList<string> UnknownIdentifiers;
  bool IsApproved

OpenSourceLicense (enum) - SPDX licenses, each tagged with
  [SpdxLicense(id, name, category, osiApproved, fsfFree)]; members include
  Mit, Mit0, Apache20, Bsd2Clause, Bsd3Clause, ZeroBsd, Isc, Zlib, Bsl10,
  Unlicense, Cc010, Unicode30, UnicodeDfs2016, Mpl20, Lgpl21Only,
  Lgpl30OrLater, Gpl20Only, Gpl30Only, Gpl30OrLater, Agpl30Only, Epl20,
  Cddl10, Artistic20, OpenSsl, PostgreSql, Ofl11, CcBy40, CcBySa40, Wtfpl,
  BlueOak100, MsPl, Ruby, Vim, X11 (and more - see the enum).
OpenSourceLicenseInfo (sealed class)
  OpenSourceLicense License; string SpdxId, Name; LicenseCategory Category;
  bool IsOsiApproved, IsFsfFree, IsOrLater
  static IReadOnlyList<OpenSourceLicenseInfo> All
  static OpenSourceLicenseInfo Get(OpenSourceLicense license)
  static string ToSpdxId(OpenSourceLicense license)
  static bool TryParse(string spdxId, out OpenSourceLicense license)
      case-insensitive; deprecated ids ("GPL-3.0", "GPL-3.0+") map to the
      -only / -or-later members
  static OpenSourceLicense Parse(string spdxId)
  static IReadOnlyList<OpenSourceLicense> InCategories(
      params LicenseCategory[] categories)
LicenseCategory: PublicDomain, Permissive, WeakCopyleft, StrongCopyleft,
  NetworkCopyleft, Other
SpdxLicenseException (sealed class): SpdxLicenseExceptionKind Kind; string
  Id; bool IsKnown; static IReadOnlyList<string> KnownExceptionIds;
  static SpdxLicenseException Parse(string id);
  static SpdxLicenseException FromKind(SpdxLicenseExceptionKind kind)
SpdxLicenseExceptionKind: Unknown, LlvmException, ClasspathException20,
  GccException31, BisonException22, AutoconfException30,
  OpenJdkAssemblyException10, LinuxSyscallNote, MifException
SpdxLicenseAttribute (attribute): string Id, Name; LicenseCategory
  Category; bool OsiApproved, FsfFree

--- Building (CodeBrix.RustTools.Building) ---

CargoRunner (sealed class) - cargo without the workbench's checks
  CargoRunner(RustWorkbenchOptions options = null)
  RustWorkbenchOptions Options; event EventHandler<CargoProgress> Progress
  Task<CargoCommandResult> RunAsync(string projectDirectory,
      CargoCommand command, CargoBuildOptions options = null,
      IProgress<CargoProgress> progress = null,
      CancellationToken cancellationToken = default)
  BuildAsync / CheckAsync / CleanAsync / DocAsync / ClippyAsync / FmtAsync /
  FetchAsync
      (string projectDirectory, CargoBuildOptions options = null,
       IProgress<CargoProgress> progress = null,
       CancellationToken cancellationToken = default)
  Task<CargoTestResult> TestAsync(...same parameters...)
  projectDirectory may be a folder or a Cargo.toml path. CargoRunner
  ignores LicensePolicy and does not check readiness.

CargoCommand: Build, Check, Test, Clean, Doc, Clippy, Fmt, Fetch
CargoProfile: Debug (no flag), Release (--release)

CargoBuildOptions (sealed class) - one option per cargo flag
  CargoProfile Profile = Debug; string CustomProfileName (--profile);
  IList<string> Features, Packages (-p), Bins, Examples, ExtraArguments;
  bool AllFeatures, NoDefaultFeatures, AllTargets, Locked, Offline, Frozen,
  Verbose (-v), KeepRawOutput; string TargetTriple (--target), TestNameFilter,
  WorkingDirectory; string MacOSInstallName (the install name of a cdylib
  built for a macOS target through a MacOSLinkProfile, e.g.
  "@rpath/libmy_lib.dylib"; null = the output path); int? Jobs (-j; 0
  rejected), ExpectedCrateCount (null = the workbench estimates
  CargoProgress.Total when a progress receiver or a LicensePolicy is
  present; a value you set is kept);
  IDictionary<string,string> Environment; TimeSpan? Timeout;
  bool ThrowOnCancellation = true; bool NoFailFast (test);
  bool CheckOnly = true (fmt --check); bool NoDeps = true (doc --no-deps)
  LicensePolicy LicensePolicy          null = no gate (RustWorkbench only)
  bool ThrowOnLicenseViolation = true  (ignored in ReportOnly)
  LicenseGateMode LicenseGateMode = Enforce
LicenseGateMode (enum, CodeBrix.RustTools.Licensing)
  Enforce       a failed audit stops the command before cargo runs (throws,
                or a failed result with LicenseGateFailed when
                ThrowOnLicenseViolation = false)
  ReportOnly    the audit runs, its report is attached to
                result.LicenseAudit whether it passed or not, cargo runs
                anyway; a failed audit never fails the result and never
                throws (LicenseGateFailed stays false)

CargoCommandResult (class)
  const int LicenseGateFailedExitCode = -2
  CargoCommand Command; bool Success; int ExitCode: 0 or more = cargo's own
  exit code (101 = failed build); -1 = cargo was killed (cancelled or timed
  out); -2 (LicenseGateFailedExitCode) = the license gate failed and cargo
  was never started; IReadOnlyList<CompilerDiagnostic>
  Diagnostics (every message, duplicates included), Warnings, Errors,
  BuildScriptWarnings (distinct); int WarningCount, ErrorCount;
  IReadOnlyList<BuildArtifact> Artifacts, RunnableArtifacts (bins and
  examples); TimeSpan Duration; string CommandLine, WorkingDirectory,
  StandardError, StandardOutput (non-JSON stdout: libtest output, rustfmt
  diff); IReadOnlyList<string> RawMessages (only with KeepRawOutput); bool
  TimedOut, WasCancelled
  LicenseAuditReport LicenseAudit       null when no gate ran; in
                                        ReportOnly set whether it passed or
                                        not (check LicenseAudit.Passed)
  bool LicenseGateFailed                true only when an Enforce gate
                                        failed and cargo did not run
  ToolchainReadiness Readiness          the workbench's readiness check for
                                        build/check/test/doc/clippy (may hold
                                        non-blocking warnings); null for
                                        clean, fmt and CargoRunner results
CargoTestResult : CargoCommandResult
  int TestsPassed, TestsFailed, TestsIgnored, TestsMeasured,
  TestsFilteredOut, TestBinaryCount; IReadOnlyList<TestOutcome> Tests;
  IReadOnlyList<string> FailedTestNames; bool AllTestsPassed
TestOutcome: string Name, IgnoreReason, Output (captured output of a failed
  test); TestStatus Status; bool Passed, Failed, Ignored
TestStatus: Passed, Failed, Ignored, Measured

CompilerDiagnostic (sealed class)
  DiagnosticLevel Level; string Code (e.g. "E0308", "unused_variables",
  "clippy::needless_return", "rustfmt"), Message, Rendered (rustc's
  human-readable text), PackageId, PackageName, TargetName;
  IReadOnlyList<CodeSpan> Spans; IReadOnlyList<CompilerDiagnostic>
  Children; DiagnosticOrigin Source; CodeSpan PrimarySpan; bool IsError
  (Error or Ice), IsWarning
DiagnosticLevel: Error, Warning, Note, Help, FailureNote, Ice
DiagnosticOrigin: Rustc, BuildScript, Cargo
CodeSpan: string FileName, Label, SuggestedReplacement; int LineStart,
  ColumnStart, LineEnd, ColumnEnd; bool IsPrimary

BuildArtifact (sealed class)
  string PackageId, PackageName, PackageVersion, ManifestPath, TargetName,
  Executable (null for libraries); IReadOnlyList<string> Kinds, Features,
  Filenames; ArtifactProfile Profile; bool Fresh, IsBuildScript, IsRunnable
ArtifactProfile: string OptLevel, DebugInfo; bool DebugAssertions,
  OverflowChecks, Test

CargoProgress (sealed class)
  CargoProgressPhase Phase; string CrateName, CrateVersion, Message; int
  Completed; int? Total (an ESTIMATE: ExpectedCrateCount, else the
  workbench's count of the target's build graph, see RunCargoAsync step 4;
  null when unknown, e.g. CargoRunner used directly); double? Fraction
  (0..1 when Total known, clamped)
CargoProgressPhase: Other, Updating, Locking, Downloading, Compiling,
  Checking, Documenting, CrateCompleted, Running, Finished, Warning, Error
  Completed counts distinct packages with a compiled (non build-script)
  artifact; a package with a lib and a bin counts once. The Finished report
  is held until cargo's build-finished message, so its Completed is final
  even when stderr (Finished) is read ahead of stdout (the artifacts).
  A failed ReportOnly license audit arrives as one Warning report before
  cargo starts.

MacOSLinkProfile (sealed class) - build macOS targets without Apple's linker
  OPT-IN, for reproducing and validating CodeBrix.RustTools.Docker's macOS
  builds on a Mac. Without it (the default of BuildAsync and of
  BuildMacOSUniversalAsync with profile = null) this library builds a macOS
  target exactly as "cargo build --target <triple>" does on the machine:
  Apple's linker and SDK on a Mac, nothing added, nothing stubbed. Apply the
  profile only when you want the container's link settings on the host.
  IMPORTANT NOTICE: macOS binaries produced by CodeBrix.RustTools.Docker are
  built in Docker containers without Apple's SDK, tools or files and are for
  development and testing only. They must not be distributed or used in any
  production environment. Any macOS build that will be distributed or run in
  production must be produced on a Mac and can be created using
  CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are
  available under Apple's own license terms.
  The same applies to binaries built on a Mac WITH this profile: they are
  for development and testing only and must not be distributed or used in
  production; a build that will be is made without the profile, with Apple's
  toolchain, SDK and code signing under Apple's own license terms.
  Links aarch64-apple-darwin / x86_64-apple-darwin with rustup's rust-lld
  (ld64 flavour) against a libSystem symbol stub, on a Mac or any other
  host with the Darwin targets installed. The stub is a text file listing
  the symbol names Rust's std imports from /usr/lib/libSystem.B.dylib,
  derived from rustup's own files, plus the ISO C math functions and the
  Darwin library calls LLVM emits for float math (pow, log, sin, fmod,
  __sincos_stret, ...; names only), so crates that do float math link; a
  CodeBrix.RustTools.Docker image with a macOS target generates it and
  ExportMacOSSysrootAsync copies it to the host. A stub exported from an
  image built before 2026-10-09 lacks the math names and fails to link such
  a crate ("undefined symbol: _pow"): export it again from a current image. A Mac and a container that build the same source with the same
  Rust release, this profile and the same stub produce byte-for-byte the
  same files (ad-hoc linker signature included).
  MacOSLinkProfile(string rustLldPath, string sysrootDirectory,
                   string cargoRegistrySourceDirectory = null)
  static MacOSLinkProfile ForToolchain(RustToolchainInfo toolchain,
      string sysrootDirectory)      rust-lld from toolchain.Sysroot and
      HostTriple, the registry folder from CargoHome; FileNotFoundException
      when rust-lld or <sysroot>/usr/lib/libSystem.tbd is missing
  string RustLldPath, SysrootDirectory, CargoRegistrySourceDirectory,
  StubPath
  const string DeploymentTarget = "11.0", RegistryRemapTarget =
      "/cargo-registry-src", StubRelativePath = "usr/lib/libSystem.tbd",
      Arm64Triple, X64Triple; static IReadOnlyList<string> Triples
  static bool IsMacOSTriple(string triple)
  static string EnvironmentTripleName(string triple)   AARCH64_APPLE_DARWIN
  IReadOnlyList<string> RustFlags(string installName = null)
      -C linker-flavor=ld64.lld -C link-arg=-S
      [--remap-path-prefix=<registry>=/cargo-registry-src]
      [-C link-arg=-install_name -C link-arg=<installName>]
  string RustFlagsText(string installName = null)   (space-separated; a
      path with spaces cannot be expressed in cargo's RUSTFLAGS variables)
  IReadOnlyDictionary<string,string> EnvironmentFor(string triple,
      string installName = null)
      CARGO_TARGET_<T>_LINKER, CARGO_TARGET_<T>_RUSTFLAGS, SDKROOT (the
      sysroot; rustc then passes -syslibroot itself and never looks for
      Xcode), MACOSX_DEPLOYMENT_TARGET
  void ApplyTo(CargoBuildOptions options, string triple,
      string installName = null)
      Sets TargetTriple, MacOSInstallName (when given) and the variables in
      options.Environment. A RUSTFLAGS variable set by the caller would
      override the profile's rustflags: do not set one.
  static string DefaultInstallName(CargoProject project)
      "@rpath/lib<name>.dylib" when the package declares exactly one
      cdylib, else null. Without an install name a cdylib records its
      build path, so two builds in different folders differ.
  Limits: pure-Rust dependency trees (a crate that compiles C needs
  Apple's headers); nothing that links an Apple framework beyond
  libSystem; ad-hoc signatures only. Binaries linked this way are for
  development and testing; a release build of macOS software is made with
  Apple's toolchain, signing identity and notarisation.

MacOSUniversalBinary (static class) - universal (fat) macOS files
  Joins single-architecture Mach-O files the way Apple's lipo does (fat
  header, slices by cputype - x86_64 first - at 4 KiB / 16 KiB alignment)
  with no external tool; the slices and their signatures are unchanged.
  IReadOnlyList<MacOSBinarySlice> Create(IEnumerable<string> slicePaths,
      string outputPath, bool overwrite = true)
      ArgumentException: fewer than two slices, a slice that is not a thin
      Mach-O, two of one architecture; IOException: output exists and
      overwrite is false
  IReadOnlyList<MacOSBinarySlice> ReadSlices(string path)   one entry for
      a thin file; FormatException for a non-Mach-O file
  bool IsUniversal(string path); bool IsMachO(string path)
  string ArchitectureName(int cpuType)        "arm64", "x86_64"
  const uint FatMagic; const int CpuTypeX64, CpuTypeArm64
MacOSBinarySlice: string Architecture; int CpuType, CpuSubtype; long
  Offset, Size; int Alignment (log2; 0 for a thin file)
MacOSUniversalFile: string Path, FileName; IReadOnlyList<string>
  SlicePaths; IReadOnlyList<MacOSBinarySlice> Slices; bool IsExecutable
MacOSUniversalBuildResult: CargoCommandResult Arm64Build, X64Build; string
  OutputDirectory; IReadOnlyList<MacOSUniversalFile> Files; bool Success
  (both builds succeeded and a file was written)

LinuxLinkProfile (sealed class) - build a Linux target of another architecture
  With a GNU cross toolchain on the machine (on Debian: gcc-<arch>-linux-gnu,
  g++-<arch>-linux-gnu, libc6-dev-<arch>-cross) the cross gcc becomes
  cargo's linker and the cc crate's compiler, the cross ar the archiver.
  The same type drives the Linux cross layers of CodeBrix.RustTools.Docker.
  LinuxLinkProfile(string toolPrefix, string binDirectory = null)
  static LinuxLinkProfile ForTriple(string triple, string binDirectory = null)
  const string X64Triple, Arm64Triple, RiscV64Triple
      (x86_64- / aarch64- / riscv64gc-unknown-linux-gnu);
  static IReadOnlyList<string> Triples
  string ToolPrefix, BinDirectory, CompilerPath, CxxCompilerPath,
  ArchiverPath, RanlibPath
  static bool IsLinuxTriple(string triple)
  static string ToolPrefixOf(string triple)   x86_64-linux-gnu / aarch64-linux-gnu / riscv64-linux-gnu
  static string RuntimeIdentifierOf(string triple)   linux-x64 / linux-arm64 / linux-riscv64
  static IReadOnlyList<string> DebianPackagesOf(string triple)
  IReadOnlyDictionary<string,string> EnvironmentFor(string triple)
      CC_<t>, CXX_<t>, AR_<t>, RANLIB_<t>, CARGO_TARGET_<T>_LINKER,
      CARGO_TARGET_<T>_AR
  void ApplyTo(CargoBuildOptions options, string triple)
      Sets TargetTriple and the variables; the target's standard library
      must be installed (rustup target add) and the tools on PATH or in
      binDirectory. ApplyTo itself checks nothing; RunCargoAsync /
      BuildAsync check both before cargo runs (see RunCargoAsync step 1)
      and BuildLinuxAsync before its first build. Nothing is installed.

WindowsGnullvmLinkProfile (sealed class) - build Windows with llvm-mingw
  For Rust's x86_64- / aarch64-pc-windows-gnullvm targets with an unpacked
  llvm-mingw (clang, lld, mingw-w64 runtime; all open source): the clang
  wrapper becomes cargo's linker and the cc crate's compiler, llvm-ar the
  archiver, and the rustflags link the unwinder statically so the output
  imports only Windows' own DLLs (Universal C Runtime: Windows 10+). The
  same type drives the Windows layer of CodeBrix.RustTools.Docker.
  WindowsGnullvmLinkProfile(string binDirectory, bool windowsFileNames = false)
  const string X64Triple, Arm64Triple, RustFlags ("-C target-feature=+crt-static");
  static IReadOnlyList<string> Triples
  string BinDirectory, ArchiverPath, RanlibPath; bool WindowsFileNames
  static bool IsWindowsGnullvmTriple(string triple)
  static string MachineOf(string triple)              x86_64 / aarch64
  static string RuntimeIdentifierOf(string triple)    win-x64 / win-arm64
  string ClangPath(string triple), ClangPlusPlusPath(string triple)
  IReadOnlyDictionary<string,string> EnvironmentFor(string triple)
      CC_<t>, CXX_<t>, AR_<t>, RANLIB_<t>, CARGO_TARGET_<T>_LINKER,
      CARGO_TARGET_<T>_AR, CARGO_TARGET_<T>_RUSTFLAGS
  void ApplyTo(CargoBuildOptions options, string triple)
      The target's standard library must be installed (rustup target add).
      C ABI only across the MSVC boundary (Itanium C++ ABI).

AndroidLinkProfile (sealed class) - build Android targets with the NDK
  For a target and an API level, the NDK's clang wrapper becomes cargo's
  linker and the cc crate's compiler, llvm-ar the archiver - the variables
  cargo ndk sets, so cargo's JSON messages still flow. The same type drives
  the Android layer of CodeBrix.RustTools.Docker.
  AndroidLinkProfile(string toolchainBinDirectory,
                     int apiLevel = DefaultApiLevel,
                     bool windowsFileNames = false)
  static AndroidLinkProfile ForNdk(AndroidNdk ndk,
                                   int apiLevel = DefaultApiLevel)
      ArgumentOutOfRangeException when the NDK has no wrappers for the level
  const int DefaultApiLevel = 24; const string Arm64Triple
      ("aarch64-linux-android"), X64Triple ("x86_64-linux-android");
  static IReadOnlyList<string> Triples
  string ToolchainBinDirectory, ArchiverPath, RanlibPath; int ApiLevel;
  bool WindowsFileNames
  static bool IsAndroidTriple(string triple)
  static string AbiOf(string triple)                 arm64-v8a / x86_64
  static string RuntimeIdentifierOf(string triple)   android-arm64 / android-x64
  static string EnvironmentTripleName(string triple)
  string ClangPath(string triple), ClangPlusPlusPath(string triple)
  IReadOnlyDictionary<string,string> EnvironmentFor(string triple)
      CC_<t>, CXX_<t>, AR_<t>, RANLIB_<t>, CARGO_TARGET_<T>_LINKER,
      CARGO_TARGET_<T>_AR
  void ApplyTo(CargoBuildOptions options, string triple)
      Sets TargetTriple and the variables in options.Environment.
AndroidBuildOptions (sealed class): AndroidLinkProfile Profile (null = the
  detected NDK); int ApiLevel = 24; IList<string> Triples (both 64-bit
  targets); AndroidOutputLayout Layout = JniLibs; bool IncludeExecutables
AndroidOutputLayout: JniLibs (jniLibs/<abi>/), NuGetRuntimes
  (runtimes/<rid>/native/), ByTarget (<triple>/)
AndroidBuildResult: IReadOnlyDictionary<string, CargoCommandResult> Builds
  (by triple); string OutputDirectory; AndroidOutputLayout Layout;
  IReadOnlyList<AndroidBuildFile> Files; bool Success (every build
  succeeded and a file was copied)
AndroidBuildFile: string Path, FileName, Triple, Abi, RuntimeIdentifier,
  SourcePath; bool IsExecutable
LinuxBuildOptions (sealed class): LinuxLinkProfile Profile (null =
  ForTriple per target; a profile serves only the targets of its tool
  prefix); string ToolDirectory (where the cross tools are when Profile is
  null; null = PATH); IList<string> Triples (empty = every Linux target
  except the host's own); LinuxOutputLayout Layout = NuGetRuntimes;
  bool IncludeExecutables
LinuxOutputLayout: NuGetRuntimes (runtimes/<rid>/native/), ByTriple
  (<triple>/)
LinuxBuildResult: IReadOnlyDictionary<string, CargoCommandResult> Builds
  (by triple); string OutputDirectory; LinuxOutputLayout Layout;
  IReadOnlyList<LinuxBuildFile> Files; bool Success
LinuxBuildFile: string Path, FileName, Triple, RuntimeIdentifier,
  SourcePath; bool IsExecutable

--- Running (CodeBrix.RustTools.Running) ---

BuiltBinaryRunner (sealed class)
  BuiltBinaryRunner(RustWorkbenchOptions options = null)
  RustWorkbenchOptions Options
  Task<RunResult> RunAsync(BuildArtifact artifact, RunOptions options = null,
      CancellationToken cancellationToken = default)
  Task<RunResult> RunAsync(string executablePath, RunOptions options = null,
      CancellationToken cancellationToken = default)
RunOptions: IList<string> Arguments; string WorkingDirectory,
  StandardInput; IDictionary<string,string> Environment; TimeSpan? Timeout;
  bool CaptureOutput = true, ThrowOnCancellation = true; Action<string>
  OnStandardOutputLine, OnStandardErrorLine
RunResult: int ExitCode; string StandardOutput, StandardError, CommandLine;
  TimeSpan Duration; bool TimedOut, WasCancelled, Succeeded

--- Archives (CodeBrix.RustTools.Archives) ---

SourceArchiveExtractor (sealed class)
  SourceArchiveExtractor()
  static string DefaultExtractionRoot     <temp>/CodeBrix.RustTools
  ArchiveKind DetectKind(string path)     magic bytes, then extension
  ExtractedSource Extract(string archivePath,
      string destinationDirectory = null, SourceArchiveOptions options = null)
  Task<ExtractedSource> ExtractAsync(string archivePath,
      string destinationDirectory = null, SourceArchiveOptions options = null,
      CancellationToken cancellationToken = default)
  Rejects absolute paths, drive letters and ".." entries
  (SourceArchiveException); skips symbolic links, hard links and special
  files; strips a single top-level folder by default.
ArchiveKind: Unknown, Zip, Tar, TarGzip (also .tgz and .crate)
SourceArchiveOptions: bool StripSingleRootFolder = true; bool?
  DeleteOnDispose (null = only when the extractor chose the folder); long
  MaxTotalBytes = DefaultMaxTotalBytes (2 GiB); int MaxEntryCount =
  DefaultMaxEntryCount (200,000); bool Overwrite
ExtractedSource (sealed class, IDisposable)
  string ArchivePath, RootDirectory, ProjectDirectory (workspace root, else
  shallowest package; null when there is no Cargo.toml), ManifestPath,
  StrippedRootFolderName; ArchiveKind ArchiveKind; int EntryCount; long
  TotalBytes; IReadOnlyList<string> SkippedEntries; bool DeleteOnDispose
  (settable), IsDisposed; void Dispose()
ExtractAndBuildResult (sealed class, IDisposable)
  ExtractedSource Source; CargoProject Project; CargoCommandResult Build;
  bool Success; void Dispose() (disposes Source)

--- Dependencies (CodeBrix.RustTools.Dependencies) ---

DependencyInspector (sealed class)
  DependencyInspector(CargoMetadata metadata)
  CargoMetadata Metadata; IReadOnlyList<CargoMetadataPackage> Packages
  IReadOnlyList<string> GetDependencyTree(int maxDepth = int.MaxValue,
      bool includeDevDependencies = false)        `cargo tree` style lines
  IReadOnlyList<DuplicateCrate> FindDuplicateVersions()
  Task<IReadOnlyList<OutdatedCrate>> CheckOutdatedAsync(
      CratesIoClient client, CancellationToken cancellationToken = default)
DependencyReport (sealed class): CargoMetadata Metadata;
  DependencyInspector Inspector; IReadOnlyList<DuplicateCrate>
  DuplicateVersions; IReadOnlyList<OutdatedCrate> Outdated (empty when not
  checked); bool OutdatedChecked; string TreeText
DuplicateCrate: string Name; IReadOnlyList<string> Versions
OutdatedCrate: string Name, InstalledVersion, LatestVersion; bool IsYanked,
  IsOutdated


COMPLETE EXAMPLES
=================

Example 1 - Detect, evaluate and explain
----------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Toolchain;

    var workbench = new RustWorkbench();
    RustToolchainInfo toolchain =
        await workbench.DetectToolchainAsync("/src/my-app");
    Console.WriteLine(toolchain);   // one line per tool

    // clippy + rustfmt + the C toolchain; set MinimumRustVersion too if your
    // project needs one, e.g. ToolVersion.Parse(versionFromYourConfig).
    var requirements = ToolchainRequirements.WithLintAndFormat();
    ToolchainReadiness readiness =
        workbench.EvaluateReadiness(toolchain, requirements);
    if (!readiness.IsReady)
    {
        foreach (ReadinessIssue issue in readiness.Issues)
        {
            Console.WriteLine(issue);       // "Blocking: ..." / "Warning: ..."
        }

        foreach (InstallGuidance step in
                 workbench.GetInstallGuidance(readiness))
        {
            Console.WriteLine(step.Title);
            Console.WriteLine(step.Explanation);
            foreach (string command in step.Commands)
            {
                Console.WriteLine("    " + command);   // show; never run
            }
        }
    }

Example 2 - Open a project and inspect its manifest
---------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Projects;

    var workbench = new RustWorkbench();
    CargoProject project = workbench.OpenProject("/src/my-app");

    Console.WriteLine(project.IsWorkspaceRoot
        ? $"workspace with {project.WorkspaceMemberDirectories.Count} members"
        : $"{project.Name} {project.EffectivePackage.Version}, "
          + $"license {project.EffectivePackage.License}");

    foreach (CargoDependency dependency in project.Manifest.Dependencies)
    {
        Console.WriteLine($"{dependency.Kind}: {dependency}");
    }

    foreach (CargoTargetDeclaration target in project.GetDeclaredTargets())
    {
        Console.WriteLine(target);          // "bin my-app (src/main.rs)"
    }

    // Several projects in one folder tree:
    foreach (string manifest in
             CargoProjectLocator.FindProjectRoots("/src/checkouts"))
    {
        Console.WriteLine(workbench.OpenProject(manifest));
    }

Example 3 - License audit, and the license gate on a build
----------------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;
    using CodeBrix.RustTools.Licensing;
    using CodeBrix.RustTools.Projects;

    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-app");

    LicensePolicy policy = LicensePolicy.Permissive()
        .Allow(OpenSourceLicense.Mpl20)
        .Override("in_house_crate", "reviewed by legal");
    policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Warn;

    // A report on its own:
    LicenseAuditReport report =
        await workbench.AuditLicensesAsync(project, policy);
    Console.WriteLine(report.ToSummaryText());

    // As a gate: cargo is not run when the audit fails.
    var options = new CargoBuildOptions { LicensePolicy = policy };
    try
    {
        var result = await workbench.BuildAsync(project, options);
        Console.WriteLine(result.LicenseAudit.Passed);   // true here
    }
    catch (LicensePolicyViolationException ex)
    {
        Console.WriteLine(ex.Summary);
        foreach (CrateLicenseFinding finding in ex.Report.BlockingFindings)
        {
            Console.WriteLine($"{finding.CrateName}: {finding.Reason}");
        }
    }

    // Or without an exception:
    options.ThrowOnLicenseViolation = false;
    var gated = await workbench.BuildAsync(project, options);
    if (gated.LicenseGateFailed)
    {
        Console.WriteLine(gated.LicenseAudit.ToSummaryText());
    }

    // Or audit AND build in one call (report only - the build always runs):
    options.LicenseGateMode = LicenseGateMode.ReportOnly;
    var built = await workbench.BuildAsync(project, options);
    Console.WriteLine(built.Success);                  // cargo's own outcome
    if (!built.LicenseAudit.Passed)
    {
        Console.WriteLine(built.LicenseAudit.ToSummaryText());
    }

Example 4 - Build with progress and diagnostics
-----------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-app");
    var options = new CargoBuildOptions
    {
        Profile = CargoProfile.Release,
        Timeout = TimeSpan.FromMinutes(20),
    };
    options.Features.Add("json");
    var progress = new Progress<CargoProgress>(p =>
        Console.WriteLine(p.Fraction is double f ? $"{f:P0} {p.Message}"
                                                 : p.Message));

    CargoCommandResult result =
        await workbench.BuildAsync(project, options, progress);
    foreach (CompilerDiagnostic error in result.Errors)
    {
        CodeSpan at = error.PrimarySpan;
        Console.WriteLine($"{at?.FileName}:{at?.LineStart} {error.Code} "
                          + error.Message);
    }

    Console.WriteLine($"{result.WarningCount} warning(s)");
    if (result.Success)
    {
        foreach (BuildArtifact exe in result.RunnableArtifacts)
        {
            Console.WriteLine(exe.Executable);
        }
    }

Example 5 - Run the tests
-------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-app");
    CargoTestResult tests = await workbench.TestAsync(project,
        new CargoBuildOptions { NoFailFast = true });
    Console.WriteLine(
        $"{tests.TestsPassed} passed, {tests.TestsFailed} failed");
    foreach (TestOutcome failed in tests.Tests)
    {
        if (failed.Failed)
        {
            Console.WriteLine(failed.Name);
            Console.WriteLine(failed.Output);
        }
    }

Example 6 - Extract an archive, build it and run the result
-----------------------------------------------------------
    using System;
    using System.Linq;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Archives;
    using CodeBrix.RustTools.Building;
    using CodeBrix.RustTools.Running;

    var workbench = new RustWorkbench();
    // null destination = a new temp folder, deleted when disposed
    using ExtractAndBuildResult built = await workbench.ExtractAndBuildAsync(
        "/downloads/my-app-main.zip",
        options: new CargoBuildOptions { Profile = CargoProfile.Release });
    if (!built.Success)
    {
        Console.WriteLine(built.Build.StandardError);
        return;
    }

    var run = new RunOptions { Timeout = TimeSpan.FromSeconds(30) };
    run.Arguments.Add("--help");
    RunResult output = await workbench.RunAsync(
        built.Build.RunnableArtifacts.First(), run);
    Console.WriteLine(output.Succeeded ? output.StandardOutput
                                       : output.StandardError);
    // Run everything you need BEFORE the using block ends: disposing
    // deletes the folder that holds the executables.

Example 7 - Dependency health
-----------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Dependencies;

    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-app");
    DependencyReport deps =
        await workbench.InspectDependenciesAsync(project, checkOutdated: true);
    Console.WriteLine(deps.TreeText);
    foreach (DuplicateCrate duplicate in deps.DuplicateVersions)
    {
        Console.WriteLine(duplicate);        // "name: version, version"
    }

    foreach (OutdatedCrate crate in deps.Outdated)
    {
        Console.WriteLine(crate);
    }

Example 8 - Check for updates and look up a crate
-------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Registry;

    var workbench = new RustWorkbench();
    var info = await workbench.DetectToolchainAsync();
    ToolUpdateReport report = await workbench.CheckForToolUpdatesAsync(info);
    Console.WriteLine(report);
    if (report.AnyUpdateAvailable)
    {
        Console.WriteLine(report.Guidance[0]);   // "rustup update" and why
    }

    var cratesIo = new CratesIoClient(new RustWorkbenchOptions
    {
        UserAgent = "MyTool (https://example.com/contact)",
    });
    CrateInfo crate = await cratesIo.GetCrateAsync("serde");
    Console.WriteLine(crate?.MaxStableVersion);

Example 9 - A universal macOS build on a Mac
--------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    // Needs both targets: rustup target add aarch64-apple-darwin
    //                                       x86_64-apple-darwin
    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-native-lib");
    MacOSUniversalBuildResult result = await workbench.BuildMacOSUniversalAsync(
        project, "/src/my-native-lib/universal",
        new CargoBuildOptions { Profile = CargoProfile.Release });
    if (!result.Success)
    {
        Console.WriteLine(result.Arm64Build.StandardError);
        Console.WriteLine(result.X64Build.StandardError);
        return;
    }

    foreach (MacOSUniversalFile file in result.Files)
    {
        Console.WriteLine(file);   // path (x86_64 ... + arm64 ...)
    }
    // Sign and notarise with Apple's tools before distributing.

Example 10 - The same bytes as a CodeBrix.RustTools.Docker container build
--------------------------------------------------------------------------
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    // sysrootFolder: copied out of a container whose image has the macOS
    // layer, with DockerRustWorkbench.ExportMacOSSysrootAsync.
    var workbench = new RustWorkbench();
    var toolchain = await workbench.DetectToolchainAsync("/src/my-app");
    var profile = MacOSLinkProfile.ForToolchain(toolchain, sysrootFolder);
    var project = workbench.OpenProject("/src/my-app");
    var options = new CargoBuildOptions { Profile = CargoProfile.Release };
    profile.ApplyTo(options, MacOSLinkProfile.Arm64Triple,
        MacOSLinkProfile.DefaultInstallName(project));
    var result = await workbench.BuildAsync(project, options);
    // result.RunnableArtifacts[0].Executable and the .dylib in
    // result.Artifacts[..].Filenames are byte-for-byte what the container
    // produced for CrossTarget.MacOSArm64 with the same Rust release.
    // Or both architectures plus the universal file in one call:
    var universal = await workbench.BuildMacOSUniversalAsync(
        project, "/src/my-app/universal", options, profile);

Example 11 - Android libraries for an APK or a NuGet package
------------------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    // Needs the NDK (ANDROID_NDK_HOME or ANDROID_HOME) and
    // rustup target add aarch64-linux-android x86_64-linux-android
    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-native-lib");   // a cdylib
    try
    {
        AndroidBuildResult result = await workbench.BuildAndroidAsync(
            project, "/src/my-app/src/main",          // -> jniLibs/<abi>/
            new CargoBuildOptions { Profile = CargoProfile.Release });
        foreach (AndroidBuildFile file in result.Files)
        {
            Console.WriteLine(file);   // arm64-v8a: .../jniLibs/arm64-v8a/libmy_native_lib.so
        }

        // The NuGet layout instead, one ABI, API level 26:
        var nuget = new AndroidBuildOptions
        {
            Layout = AndroidOutputLayout.NuGetRuntimes,
            ApiLevel = 26,
        };
        nuget.Triples.Clear();
        nuget.Triples.Add(AndroidLinkProfile.Arm64Triple);
        await workbench.BuildAndroidAsync(project, "/src/my-package/native",
            null, nuget);   // native/runtimes/android-arm64/native/libmy_native_lib.so
    }
    catch (ToolchainNotFoundException ex)
    {
        foreach (var step in ex.Guidance)
        {
            Console.WriteLine(step);   // rustup target add ..., install the NDK
        }
    }

Example 12 - Linux ARM64 and RISC-V 64 libraries on a Linux host
----------------------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    // Debian x86-64 host: rustup target add aarch64-unknown-linux-gnu
    //   riscv64gc-unknown-linux-gnu, and apt install gcc-aarch64-linux-gnu
    //   g++-aarch64-linux-gnu libc6-dev-arm64-cross (and the riscv64 set)
    var workbench = new RustWorkbench();
    var project = workbench.OpenProject("/src/my-native-lib");   // a cdylib
    try
    {
        LinuxBuildResult result = await workbench.BuildLinuxAsync(
            project, "/src/my-package/native",
            new CargoBuildOptions { Profile = CargoProfile.Release });
        foreach (LinuxBuildFile file in result.Files)
        {
            Console.WriteLine(file);   // linux-arm64: .../runtimes/linux-arm64/native/libmy_native_lib.so
        }
    }
    catch (ToolchainNotFoundException ex)
    {
        foreach (var step in ex.Guidance)
        {
            Console.WriteLine(step);   // rustup target add ..., sudo apt install ...
        }
    }

Example 13 - Custom tool locations, environment and offline builds
------------------------------------------------------------------
    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;

    var options = new RustWorkbenchOptions
    {
        CargoHome = "/opt/rust/cargo",
        RustupHome = "/opt/rust/rustup",
        ToolProbeTimeout = TimeSpan.FromSeconds(20),
    };
    options.Environment["RUSTFLAGS"] = "-D warnings";
    var workbench = new RustWorkbench(options);
    var project = workbench.OpenProject("/src/my-app");

    // Online once: download every dependency into CARGO_HOME.
    var fetch = await workbench.FetchAsync(project,
        new CargoBuildOptions { Locked = true });
    if (!fetch.Success)
    {
        Console.WriteLine(fetch.StandardError);    // e.g. no network
        return;
    }

    // From here on nothing touches the network.
    var result = await workbench.CheckAsync(project,
        new CargoBuildOptions { Offline = true });


MINIMUM VIABLE PROJECT TEMPLATE
===============================
RustBuild.csproj:

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.RustTools.MitLicenseForever"
                          Version="*" />
      </ItemGroup>
    </Project>

  (Pin Version to the current published version for reproducible builds.)

Program.cs:

    using System;
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;
    using CodeBrix.RustTools.Licensing;

    var folder = args.Length > 0 ? args[0] : ".";
    var workbench = new RustWorkbench();
    try
    {
        var project = workbench.OpenProject(folder);
        var options = new CargoBuildOptions
        {
            LicensePolicy = LicensePolicy.Permissive(),
        };
        var result = await workbench.BuildAsync(project, options,
            new Progress<CargoProgress>(p => Console.WriteLine(p)));
        Console.WriteLine(result);
        foreach (var error in result.Errors)
        {
            Console.WriteLine(error.Rendered);
        }
    }
    catch (ToolchainNotFoundException ex)
    {
        foreach (var step in ex.Guidance)
        {
            Console.WriteLine(step);
        }
    }
    catch (LicensePolicyViolationException ex)
    {
        Console.WriteLine(ex.Summary);
    }


PERFORMANCE TIPS
================
- Detection starts several processes. A RustWorkbench caches it per
  project folder (and rust-toolchain.toml content), so its compiling
  commands detect once per folder; reuse one workbench rather than creating
  one per build. Set ToolchainCacheDuration in long-running hosts.
- Compiling commands run one `cargo metadata` before cargo (shared by the
  license gate and the CargoProgress.Total estimate) when a LicensePolicy
  is set, or when a progress receiver is present and ExpectedCrateCount is
  not set. For repeated builds of the same project, audit once with
  AuditLicensesAsync and build without LicensePolicy and with
  ExpectedCrateCount set (or without progress) to skip it.
- Keep the project's target/ folder between builds; cargo then rebuilds only
  what changed (BuildArtifact.Fresh tells you which artifacts were reused).
- Run FetchAsync once, then set Offline (or Frozen) for the commands that
  follow: it avoids index updates and makes failures immediate.
- crates.io requests are spaced by CratesIoRequestInterval across the whole
  process; checkOutdated on a large graph takes about a second per crate.
- The stable channel manifest is large; cache the ToolUpdateReport rather
  than checking on every start-up.


COMMON PITFALLS TO AVOID
========================
- Do not run InstallGuidance.Commands automatically. They are written for a
  person at a terminal: several need elevation (RequiresAdministrator) or a
  new shell (RequiresNewShell), and lines starting with '#' are comments.
- Every process runs with RUSTUP_AUTO_INSTALL=0, so a project whose
  rust-toolchain.toml names an uninstalled toolchain FAILS instead of
  downloading it. Readiness reports ToolchainNotInstalled; show the
  InstallToolchain guidance. Pass the project folder to DetectToolchainAsync
  or the override is neither applied nor reported.
- Readiness checks the C toolchain. build / check / test / doc / clippy
  through RustWorkbench throw ToolchainNotFoundException (cargo never
  starts) when the C compiler / linker is Missing; the exception's Guidance
  says what to install. Unknown (could not be determined) only adds a
  warning to result.Readiness. Use CargoRunner directly, or
  EvaluateReadiness with RequireCCompiler = false, if you must skip it.
- Detection is cached per workbench. After installing or updating a tool
  while a workbench is alive, call RefreshToolchainAsync (or
  InvalidateToolchainCache), or the workbench keeps the old answer.
- Exit codes: -1 means cargo was killed (cancelled / timed out), -2
  (CargoCommandResult.LicenseGateFailedExitCode) means the license gate
  failed and cargo never ran; 0 and above are cargo's own.
- The library never installs anything - including clippy and rustfmt. A
  missing component makes ClippyAsync / FmtAsync throw CargoCommandException
  whose Guidance says how to add it.
- The license gate is applied by RustWorkbench only. CargoRunner ignores
  CargoBuildOptions.LicensePolicy.
- LicenseGateMode.ReportOnly: result.LicenseGateFailed stays false even
  when the audit failed - read result.LicenseAudit.Passed instead.
- The gate audits the graph of the build's target only (--filter-platform),
  so a crate pulled in only on another platform is not audited for this
  build; audit with AuditLicensesAsync (no filter) to cover every platform.
- Unresolved is not Rejected: a crate with only a license-file, no license,
  an unknown identifier or an unparsable expression is Unresolved, and the
  default UnresolvedLicenseHandling.Reject fails the audit. Use
  policy.Override(name, reason) for crates you reviewed by hand, or Warn /
  Allow.
- Workspace members are exempt by default (ExemptWorkspaceMembers = true);
  set it to false to check your own crates' licenses too.
- `cargo metadata` needs every dependency's manifest. With Offline it works
  only when the lockfile's crates are already in the local registry cache;
  otherwise it fails with CargoCommandException (the gate inherits the
  build's Offline / Locked / Frozen settings). The same goes for an Offline
  build, which fails with cargo's "no matching package named ... found".
  Call FetchAsync first (online) to fill the cache, or leave Offline off
  and let cargo download what it needs.
- A failed compile is NOT an exception: check result.Success and
  result.Errors. Exceptions mean the command could not run (missing tools,
  license gate, missing component, cancellation).
- Cancellation kills the whole process tree (cargo, rustc, build scripts,
  linkers, or the program you ran) and throws OperationCanceledException;
  set ThrowOnCancellation = false to get a result with WasCancelled instead.
  A Timeout sets TimedOut and never throws.
- Diagnostics holds every message including duplicates (rustc reports a
  warning once per target); Warnings and Errors are the distinct ones.
- CargoProgress.Total is an estimate (feature unification and per-target
  details can make Completed end a little off it); clamp UI progress to
  Fraction. Estimating costs one `cargo metadata` before a compiling
  command, and only happens when a progress receiver (a progress argument
  or a Progress event handler) is present and ExpectedCrateCount is not
  set; without a receiver and without a policy no metadata call is made
  and Total stays null.
- Archives: the extractor refuses entries that would escape the folder,
  skips links, and enforces MaxTotalBytes / MaxEntryCount - raise them in
  SourceArchiveOptions for very large sources (pass them to
  SourceArchiveExtractor or to the ExtractAndBuildAsync overload that takes
  SourceArchiveOptions; the shorter overload uses the defaults).
- Disposing an ExtractAndBuildResult deletes the extracted folder, and with
  it the built executables (when the extractor chose the folder). Run the
  programs first, or set Source.DeleteOnDispose = false.
- crates.io rejects requests without an identifying User-Agent. Set
  RustWorkbenchOptions.UserAgent to identify your application.
- RustWorkbenchOptions.HttpClient / HttpMessageHandler are read when a
  workbench or client is CONSTRUCTED.
- ToolVersion equality ignores the tool name, commit and raw text; compare
  RustReleaseInfo.CargoVersion (not CargoPackageVersion) with the installed
  cargo.
- MacOSLinkProfile builds are for development and testing: they carry the
  linker's ad-hoc signature, not an identity, and need pure-Rust dependency
  trees. Do not set RUSTFLAGS in the options' Environment alongside a
  profile (it overrides the profile's rustflags), and set an install name
  for a cdylib (or let DefaultInstallName do it) or the file embeds its
  build path.
- BuildMacOSUniversalAsync needs both Darwin targets installed; the
  ToolchainNotFoundException it throws otherwise carries the
  `rustup target add` guidance - show it, do not run it.
- Android needs the NDK to be FOUND: set ANDROID_NDK_HOME (or ANDROID_HOME
  for the SDK) or RustWorkbenchOptions.AndroidNdkPath; an SDK installed in a
  custom folder is not found by itself. RustToolchainInfo.AndroidNdk tells
  you what detection found. On Linux and Windows the NDK runs on x86-64
  only; AndroidNdkLocator.HostSupportMessage explains a refusal.
- A Linux build for another architecture needs BOTH the target's standard
  library (rustup target add) and the GNU cross toolchain (on Debian
  gcc-/g++-<arch>-linux-gnu and libc6-dev-<arch>-cross). Without them
  cargo fails with "can't find crate for `std`" (exit 101) or a linker
  error; BuildLinuxAsync and BuildAsync with a LinuxLinkProfile applied
  catch both first and throw ToolchainNotFoundException with that
  guidance. A plain BuildAsync with only TargetTriple set (no profile, so
  no CARGO_TARGET_<T>_LINKER) checks the standard library only: apply a
  LinuxLinkProfile, or cargo links with the host's cc. Never apply a
  profile to the host's own triple; BuildLinuxAsync rejects it.
- The API level is part of the link (the wrapper name): pick the lowest
  Android version your app supports (AndroidBuildOptions.ApiLevel, default
  24) and keep it the same for every ABI.


WHAT THIS PACKAGE DOES NOT DO
=============================
- It does not install, update or remove Rust, rustup, components, targets or
  C toolchains, and it never runs the commands in InstallGuidance.
- It does not build inside containers. For that, use the sibling package
  CodeBrix.RustTools.Docker.MitLicenseForever: it runs the same cargo
  commands, license gate and result model inside a Linux Docker container,
  so the machine needs Docker but no Rust tools, and it adds Windows (GNU)
  and Android cross-compilation. Its own AGENT-README.txt describes it.
- It does not cross-compile beyond passing --target to cargo (the target
  and its linker must already be installed) - except for macOS, where
  MacOSLinkProfile supplies rustup's own linker and a libSystem stub, so
  the Darwin targets build on any host that has them installed, for
  Android, where AndroidLinkProfile points cargo at an installed NDK, and
  for Linux of another architecture, where LinuxLinkProfile points cargo at
  an installed GNU cross toolchain, and for Windows through the gnullvm
  targets, where WindowsGnullvmLinkProfile points cargo at an unpacked
  llvm-mingw.
- It does not install the Android NDK or SDK, and it does not package an
  APK or AAR: it writes the jniLibs/ folder an app module consumes.
- It does not sign or notarise macOS binaries; universal files keep their
  slices' ad-hoc linker signatures.
- It does not publish crates, edit Cargo.toml or Cargo.lock, or write to
  crates.io; registry access is read-only.
- It does not decide legal questions: the license gate checks declared SPDX
  metadata against your policy, it does not read license texts.
- It does not support operating systems other than Windows, macOS and Linux;
  apt-specific guidance is given only for Debian-based distributions.


WORKING EXAMPLES ON GITHUB
==========================
The test project exercises every feature area:
https://github.com/ellisnet/CodeBrix.RustTools/tree/main/
    tests/CodeBrix.RustTools.Tests
(paths below are relative to that folder)

  Facade: toolchain + updates .... RustWorkbenchTests.cs
  Facade: build, test, run, deps . RustWorkbenchBuildTests.cs
  Facade: license gate ........... RustWorkbenchLicenseGateTests.cs
  Facade: extract and build ...... RustWorkbenchArchiveTests.cs
  Facade: readiness before cargo . RustWorkbenchReadinessTests.cs
  Facade: detection cache ........ RustWorkbenchToolchainCacheTests.cs
  Detection ...................... Toolchain/RustToolchainDetectorTests.cs
  Versions ....................... Toolchain/ToolVersionTests.cs
  rust-toolchain.toml ............ Toolchain/RustToolchainFileTests.cs
  Readiness ...................... Toolchain/ToolchainReadinessTests.cs
  Install / update guidance ...... Toolchain/InstallGuidanceProviderTests.cs
  Operating-system detection ..... Toolchain/HostOperatingSystemTests.cs
  Release manifests .............. Registry/RustReleaseClientTests.cs
  crates.io ...................... Registry/CratesIoClientTests.cs
  Update reports ................. Registry/ToolUpdateReportTests.cs
  Cargo.toml ..................... Projects/CargoManifestTests.cs
  Cargo.lock ..................... Projects/CargoLockFileTests.cs
  cargo metadata ................. Projects/CargoMetadataTests.cs
  Projects and workspaces ........ Projects/CargoProjectTests.cs
  Project discovery .............. Projects/CargoProjectLocatorTests.cs
  SPDX expressions ............... Licensing/SpdxLicenseExpressionTests.cs
  License list ................... Licensing/OpenSourceLicenseInfoTests.cs
  Policies ....................... Licensing/LicensePolicyTests.cs
  Audits ......................... Licensing/LicenseAuditorTests.cs
  Audit reports .................. Licensing/LicenseAuditReportTests.cs
  Cargo commands ................. Building/CargoRunnerTests.cs
  Build options .................. Building/CargoBuildOptionsTests.cs
  Command results ................ Building/CargoCommandResultTests.cs
  Diagnostics and artifacts ...... Building/CargoJsonMessageParserTests.cs
  Output, tests, progress ........ Building/CargoOutputCollectorTests.cs
  Progress ....................... Building/CargoProgressTests.cs
  Running programs ............... Running/BuiltBinaryRunnerTests.cs
  macOS link profile ............. Building/MacOSLinkProfileTests.cs
  Universal (fat) files .......... Building/MacOSUniversalBinaryTests.cs
  Universal build on a Mac ....... RustWorkbenchMacOSUniversalTests.cs
  NDK discovery .................. Toolchain/AndroidNdkLocatorTests.cs
  Android link profile ........... Building/AndroidLinkProfileTests.cs
  Linux cross link profile ....... Building/LinuxLinkProfileTests.cs
  Windows gnullvm link profile ... Building/WindowsGnullvmLinkProfileTests.cs
  Android build (jniLibs, NuGet) . RustWorkbenchAndroidTests.cs
  Linux cross build (NuGet) ...... RustWorkbenchLinuxTests.cs
  Archives ....................... Archives/SourceArchiveExtractorTests.cs
  Dependency health .............. Dependencies/DependencyInspectorTests.cs
  Exceptions ..................... RustToolsExceptionTests.cs


QUICK REFERENCE CARD
====================
--- Setup ---
Workbench:      var wb = new RustWorkbench(options /* or null */);
Progress event: wb.Progress += (_, p) => Log(p);

--- Toolchain ---
Detect:         var info = await wb.DetectToolchainAsync(projectDir);
Re-detect:      await wb.RefreshToolchainAsync(projectDir);  (cached per wb)
One tool:       info.Cargo.Status / .Version / .ExecutablePath / .Message
Ready?          var r = wb.EvaluateReadiness(info); r.IsReady, r.Issues
Guidance:       foreach (var g in wb.GetInstallGuidance(r)) Show(g);
Updates:        var u = await wb.CheckForToolUpdatesAsync(info);

--- Projects ---
Open:           var p = wb.OpenProject(folderOrCargoToml);
Manifest:       p.Manifest.Dependencies, p.EffectivePackage.License
Targets:        p.GetDeclaredTargets()
Metadata:       await p.GetMetadataAsync(new CargoMetadataOptions())
Find projects:  CargoProjectLocator.FindProjectRoots(folder)

--- Licenses ---
Policy:         LicensePolicy.Permissive().Allow(...).Override(name, why)
Audit:          var a = await wb.AuditLicensesAsync(p, policy); a.Passed
Gate a build:   new CargoBuildOptions { LicensePolicy = policy }

--- Cargo ---
Build:          var b = await wb.BuildAsync(p, options, progress, ct);
Others:         wb.CheckAsync / TestAsync / CleanAsync / DocAsync /
                ClippyAsync / FmtAsync / RunCargoAsync(p, command, ...)
Download deps:  await wb.FetchAsync(p);  then Offline = true needs no network
Outcome:        b.Success, b.Errors, b.Warnings, b.RunnableArtifacts
Tests:          t.TestsPassed, t.TestsFailed, t.FailedTestNames

--- Linux, another architecture (a Debian host with the cross packages) ---
Both foreign:   var l = await wb.BuildLinuxAsync(p, outDir, options);
                                    // runtimes/<rid>/native/, readiness first
By hand:        LinuxLinkProfile.ForTriple(LinuxLinkProfile.Arm64Triple)
                    .ApplyTo(options, LinuxLinkProfile.Arm64Triple);
                await wb.BuildAsync(p, options);

--- Windows via llvm-mingw (any host with llvm-mingw unpacked) ---
                new WindowsGnullvmLinkProfile("/opt/llvm-mingw/bin")
                    .ApplyTo(options, WindowsGnullvmLinkProfile.Arm64Triple);
                await wb.BuildAsync(p, options);

--- Android ---
APK libraries:  var a = await wb.BuildAndroidAsync(p, "app/src/main", options);
NuGet layout:   new AndroidBuildOptions { Layout = AndroidOutputLayout.NuGetRuntimes }
By hand:        AndroidLinkProfile.ForNdk(info.AndroidNdk).ApplyTo(options,
                    AndroidLinkProfile.Arm64Triple); await wb.BuildAsync(p, options);
Find the NDK:   info.AndroidNdk  /  AndroidNdkLocator.Find()

--- macOS ---
Universal:      var u = await wb.BuildMacOSUniversalAsync(p, outDir, options);
Like a container: var prof = MacOSLinkProfile.ForToolchain(info, sysrootDir);
                prof.ApplyTo(options, MacOSLinkProfile.Arm64Triple,
                             MacOSLinkProfile.DefaultInstallName(p));
Fat file by hand: MacOSUniversalBinary.Create(new[] { arm64, x64 }, outPath)
Slices:         MacOSUniversalBinary.ReadSlices(path)

--- Run, archives, dependencies ---
Run:            await wb.RunAsync(b.RunnableArtifacts[0], runOptions);
Archive:        using var x = await wb.ExtractAndBuildAsync(zipPath);
Archive limits: wb.ExtractAndBuildAsync(zipPath, dest, archiveOptions, ...)
Dependencies:   var d = await wb.InspectDependenciesAsync(p, true);

Target: .NET 10 or later
License: MIT


================================================================================
END OF AGENT-README
