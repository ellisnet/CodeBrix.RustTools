================================================================================
AGENT-README: CodeBrix.RustTools.CrateBuilder
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.RustTools.CrateBuilder.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.RustTools.CrateBuilder is a .NET 10 library that takes a Rust crate
- a Cargo.toml on this machine, or a Git repository plus the path of a
Cargo.toml inside it - and turns it into native binaries for a chosen set of
.NET runtime identifiers, a JSON build record and, optionally, a .NET 10
class library project that wraps the crate for other .NET projects.

It builds either with the Rust toolchain installed on this machine
(CodeBrix.RustTools) or inside a Linux Docker container
(CodeBrix.RustTools.Docker). It NEVER installs anything: it checks first
whether the machine can build the request and, when it cannot, returns the
install guidance of those two packages for YOUR application to show.

Feature areas:
  - Readiness first: per requested target, can this machine build it in the
    chosen mode (host toolchain, cross tools, NDK, llvm-mingw, macOS link
    profile; or the Docker engine and its capabilities)? Unbuildable targets
    are reported with guidance; the others are built.
  - Sources: a local Cargo.toml (or its folder), or a Git repository cloned
    with CodeBrix.LibGit2 at a commit, branch or tag. The crate's own folder
    is never written to: the build runs in a staged copy.
  - Nine targets: win-x64, win-arm64, osx-x64, osx-arm64, linux-x64,
    linux-arm64, linux-riscv64, android-x64, android-arm64.
  - Output: native files in the NuGet runtimes/<rid>/native/ layout (or
    Flat for one target) plus crate-build-record.json beside them.
  - A generated C facade for plain Rust libraries (rlib): the crate's
    public API is read, mapped to C (objects, structs, enums, strings,
    slices, Option, Result) and compiled into a cdylib; a crate that already
    exports C functions is built as it is.
  - A wrapper class library (optionally packable) with an idiomatic C# API
    and a loader that finds the right native file per RID, plus
    WRAP-REPORT.txt saying what was wrapped and what was not.
  - Optional: linking against the CodeBrix WebGPU engine
    (CodeBrix.WebGpu.Native).

QUICK START (the novice path: a URL in, a wrapper project out)
---------------------------------------------------------------
    using System;
    using CodeBrix.RustTools.CrateBuilder;

    using var builder = new CrateBuilder();
    var result = await builder.BuildAsync(new CrateBuildRequest
    {
        Source = new GitRepositorySource("https://github.com/owner/repo", "crates/geo/Cargo.toml"),
        Targets = { CrateTarget.LinuxX64, CrateTarget.AndroidArm64 },
        OutputDirectory = "/work/geo/native",
        Wrapper = new WrapperOptions { AssemblyName = "Acme.Geo", RootNamespace = "Acme.Geo" },
    });
    Console.WriteLine(result);   // per-RID outcome; result.Wrapper.ProjectPath is the .csproj

Then reference /work/geo/Acme.Geo/Acme.Geo.csproj from your application
(<ProjectReference>) and call the crate through its C# types: objects are
IDisposable classes, Copy structs are record structs, errors are exceptions.
Read /work/geo/native/WRAP-REPORT.txt for what was wrapped, what was not and
why, and the build record crate-build-record.json beside it for what was
built from what. Nothing about Rust, C or P/Invoke is needed on your side.

What happens (in this order, see "PIPELINE"): readiness check -> clone ->
staged copy -> public API read with a small syn-based extractor -> C mapping
planned -> C facade crate generated (for a plain Rust library) -> cargo per
target -> every native file verified -> wrapper project written -> record.

INSTALLATION
============
PackageId:  CodeBrix.RustTools.CrateBuilder.MitLicenseForever

    dotnet add package CodeBrix.RustTools.CrateBuilder.MitLicenseForever

There is no package named plain "CodeBrix.RustTools.CrateBuilder"; that is
the assembly and root namespace name.

Dependencies (pulled in automatically; the package pins the versions it was
built against):
  - CodeBrix.RustTools.MitLicenseForever          (host builds; MIT)
  - CodeBrix.RustTools.Docker.MitLicenseForever   (container builds; MIT)
  - CodeBrix.LibGit2.MitLicenseForever            (Git clones; MIT, with
                                                   the native libgit2 under
                                                   GPLv2 with the linking
                                                   exception)
  - CodeBrix.WebGpu.Native.MitLicenseForever      (the WebGPU engine's
                                                   headers and binaries; MIT)
  - CodeBrix.Templating.BsdLicenseForever         (code generation;
                                                   BSD-2-Clause)
  - CodeBrix.Docker and CodeBrix.TomlParse arrive through the first two.

License:       MIT
Requirements:  .NET 10 or later. Host mode needs Rust (cargo, rustc) and a C
               toolchain on the machine, plus per target what
               CodeBrix.RustTools needs for it (see READINESS). Docker mode
               needs a reachable Docker engine in Linux-container mode and no
               Rust tools; images other than the plain rust image are built
               with the docker CLI (buildx).


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.RustTools.CrateBuilder;   // every public type below

Types from the sibling packages that appear in this API:
    CodeBrix.RustTools                  RustWorkbenchOptions,
                                        RustToolsException
    CodeBrix.RustTools.Toolchain        RustToolchainInfo, InstallGuidance
    CodeBrix.RustTools.Building         CargoProgress, CargoCommandResult
    CodeBrix.RustTools.Licensing        LicensePolicy
    CodeBrix.RustTools.Docker           DockerRustWorkbenchOptions
    CodeBrix.RustTools.Docker.Artifacts ArtifactLayout
    CodeBrix.RustTools.Docker.Engine    DockerEngineInfo, DockerGuidance
    CodeBrix.RustTools.Docker.Images    CrossTarget
    CodeBrix.LibGit2.Handlers           CredentialsHandler

The readiness issue of this package is BuildReadinessIssue, so it never
clashes with CodeBrix.RustTools.Toolchain.ReadinessIssue (the toolchain-level
issue many of them are made from): both namespaces can be imported together.


CORE API REFERENCE
==================
Notation: "T Name" is a property with get and set; "= x" is its default.

CrateBuilder (sealed class, IDisposable) - the entry point
---------------------------------------------------------
  CrateBuilder(CrateBuilderOptions options = null)
  CrateBuilderOptions Options { get; }
  event EventHandler<CrateBuildProgress> Progress
  Task<CrateReadinessReport> CheckReadinessAsync(CrateBuildRequest request,
      CancellationToken cancellationToken = default)
  Task<CrateBuildResult> BuildAsync(CrateBuildRequest request,
      IProgress<CrateBuildProgress> progress = null,
      CancellationToken cancellationToken = default)
  void Dispose()      releases the Docker workbench's client

  - Keep one instance: it owns a host workbench (toolchain detection is
    cached per instance) and a Docker workbench, created on first use.
  - Both methods validate the request first (ArgumentException) and work on
    a copy; the caller's request is never changed.
  - BuildAsync stages, in order: Readiness, ResolvingSource, Staging,
    ExtractingApi / Planning / RenderingFacade (FacadeMode, see PIPELINE:
    by default when a wrapper is asked for or the crate does not declare a
    cdylib, and as an automatic second pass when a cdylib built as it is
    exports nothing), Building, Verifying,
    Wrapping (only with Wrapper), Recording, CleaningUp. Every stage reports
    progress (CrateBuildProgress), the build stage once per target and with
    cargo's own progress.
  - A stage that fails is a RESULT (FailedStage, FailureMessage), never an
    exception. Exceptions: ArgumentException / ArgumentNullException (bad
    request), ObjectDisposedException, OperationCanceledException (your
    token; cleanup has run).
  - A failed readiness check ends the build with FailedStage = Readiness and
    no record. Every later failure still writes the record and cleans up.
  - Targets the readiness check finds unbuildable are NotBuildableInThisMode
    (with Guidance); the others are built. Success is true only when no
    stage failed and every requested target Compiled.
  - Request.Timeout limits everything after Readiness; on expiry the result
    names the stage that was running ("timed out").

CrateBuilderOptions (sealed class)
  RustWorkbenchOptions HostOptions              null = defaults
  DockerRustWorkbenchOptions DockerOptions      null = defaults
  string ScratchRootDirectory                   null = <temp>/CodeBrix.RustTools.CrateBuilder
  bool KeepScratch = false                      keep each run's scratch folder
  string LlvmMingwDirectory                     host mode, Windows targets:
                                                llvm-mingw's folder or its bin/;
                                                null = LLVM_MINGW_HOME, then PATH
  string MacOSSysrootDirectory                  host mode on a machine that is
                                                not a Mac, macOS targets: the
                                                folder with usr/lib/libSystem.tbd
                                                (DockerRustWorkbench
                                                .ExportMacOSSysrootAsync)
  string WebGpuNativePackageDirectory           LinkToCodeBrixWebGpu: the
                                                CodeBrix.WebGpu.Native package
                                                folder (include/ + runtimes/);
                                                null = NUGET_PACKAGES, then
                                                ~/.nuget/packages (NuGet never
                                                copies include/ into an app),
                                                then (libraries only) your
                                                app's own runtimes/ folder
  string CargoTargetDirectory                   null = each run builds in its own
                                                folder under the scratch root; set =
                                                one cargo target folder shared by
                                                every build (see SHARED CARGO TARGET
                                                FOLDER):
                                                new CrateBuilderOptions {
                                                  CargoTargetDirectory = "/work/cargo-target" }
  string EffectiveScratchRootDirectory { get; }
  const string DefaultScratchFolderName

CrateBuildRequest (sealed class)
  CrateSource Source                REQUIRED
  BuildMode Mode = Host
  IList<CrateTarget> Targets        REQUIRED, non-empty, no repeats
  LinuxBaseline LinuxBaseline = RustImage   (ManyLinux: Docker mode only)
  bool LinkToCodeBrixWebGpu = false
  string OutputDirectory            REQUIRED
  ArtifactLayout Layout = NuGetRuntimes     (Flat: one target only;
                                             ByTarget: rejected)
  CrateBuildProfile Profile = Release
  IList<string> Features; bool NoDefaultFeatures, AllFeatures
  bool Offline, Locked
  LicensePolicy LicensePolicy       null = no licence gate
  TimeSpan? Timeout                 positive when set
  WrapperOptions Wrapper            null = native files + record only
  FacadeMode FacadeMode = Auto      Auto | Never | Always (see PIPELINE);
                                    Never with a Wrapper is rejected
  void Validate()                   ArgumentException naming the rule
  CrateBuildRequest Clone()         deep copy (credentials callback shared)

CrateSource (abstract class) and its two kinds
  LocalManifestSource : CrateSource
    LocalManifestSource(), LocalManifestSource(string manifestPath)
    string ManifestPath             REQUIRED: a Cargo.toml or its folder
  GitRepositorySource : CrateSource
    GitRepositorySource(), GitRepositorySource(string url,
        string manifestPath = "Cargo.toml")
    const string DefaultManifestPath = "Cargo.toml"
    string Url                      REQUIRED
    string ManifestPath = "Cargo.toml"   relative, no "..", not rooted
    string Commit, Branch, Tag      at most one; none = default branch
    string CloneDirectory           null = a folder under the scratch root
    bool CleanUpAfterBuild = false
    bool RecurseSubmodules = false
    CredentialsHandler CredentialsProvider   null = anonymous

WrapperOptions (sealed class)
  string AssemblyName, RootNamespace    REQUIRED (file-name safe; dotted C#
                                        identifier)
  WrapperPackaging Packaging            null = plain class library
  WrapPlan Plan                         null = wrap everything mappable
  string OutputDirectory                null = sibling "<AssemblyName>" of
                                        the request's OutputDirectory
  bool GenerateSmokeTests = false
  bool GenerateFieldAccessors = true    false = no Get<Field>() / Set<Field>(value)
                                        for objects' public fields (data-heavy
                                        crates); the constructor from the fields,
                                        Variant() and the per-variant constructors
                                        stay: new WrapperOptions { AssemblyName = "A",
                                        RootNamespace = "A", GenerateFieldAccessors = false }
  void Validate()
WrapperPackaging (sealed class)
  string PackageId                      REQUIRED (NuGet id characters)
  string LicenseExpression = "MIT", Authors, Description, ProjectUrl,
  RepositoryUrl, Copyright; IList<string> Tags
  void Validate()
WrapPlan (sealed class) - data only
  IList<string> IncludeTypes, ExcludeTypes, IncludeFunctions,
  ExcludeFunctions (globs over Rust paths); IDictionary<string,string>
  Renames (Rust path -> C# name); IList<FacadeOverride> Overrides;
  IList<string> AdditionalFacadeFiles; string Notes
FacadeOverride (sealed class)
  string RustPath, RustSource, CSharpSource

Enums
  BuildMode:           Host, Docker
  CrateTarget:         WinX64, WinArm64, OsxX64, OsxArm64, LinuxX64,
                       LinuxArm64, LinuxRiscV64, AndroidX64, AndroidArm64
  LinuxBaseline:       RustImage, ManyLinux
  CrateBuildProfile:   Release, Debug
  ArtifactProvenance:  DevelopmentBuild, Distributable
  CrateTargetOutcome:  Compiled, Failed, NotBuildableInThisMode, Skipped
  CrateBuildStage:     Readiness, ResolvingSource, Staging, ExtractingApi,
                       Planning, RenderingFacade, Building, Verifying,
                       Wrapping, Recording, CleaningUp

CrateTargets (static class)
  IReadOnlyList<CrateTarget> All, MacOS, Windows, Linux, Android
  string Rid(CrateTarget t)
  string Triple(CrateTarget t)
  string NativeFileName(CrateTarget t, string crateName)
  CrateTarget FromRid(string rid)       case-insensitive; ArgumentException
  CrossTarget ToCrossTarget(CrateTarget t)
  bool IsMacOS / IsWindows / IsLinux / IsAndroid(CrateTarget t)

    Target        RID            Triple                       Library file
    WinX64        win-x64        x86_64-pc-windows-gnullvm    my_crate.dll
    WinArm64      win-arm64      aarch64-pc-windows-gnullvm   my_crate.dll
    OsxX64        osx-x64        x86_64-apple-darwin          libmy_crate.dylib
    OsxArm64      osx-arm64      aarch64-apple-darwin         libmy_crate.dylib
    LinuxX64      linux-x64      x86_64-unknown-linux-gnu     libmy_crate.so
    LinuxArm64    linux-arm64    aarch64-unknown-linux-gnu    libmy_crate.so
    LinuxRiscV64  linux-riscv64  riscv64gc-unknown-linux-gnu  libmy_crate.so
    AndroidX64    android-x64    x86_64-linux-android         libmy_crate.so
    AndroidArm64  android-arm64  aarch64-linux-android        libmy_crate.so
  The triples are CodeBrix.RustTools.Docker's (CrossTargetInfo) in BOTH
  modes: Windows is always the gnullvm target (llvm-mingw, Universal C
  Runtime), never MSVC. '-' in the crate name becomes '_', as cargo does.

Results (each type's ToString() is a readable summary: CrateBuildResult and
CrateReadinessReport multi-line, the others one line)
  CrateBuildProgress: CrateBuildStage Stage; string Message; CrateTarget?
    Target; CargoProgress CargoProgress (null unless from cargo)
  CrateReadinessReport: bool IsReady; BuildMode Mode;
    IReadOnlyList<BuildReadinessIssue> Issues (blocking first);
    IReadOnlyList<InstallGuidance> Guidance; IReadOnlyList<DockerGuidance>
    DockerGuidance; IReadOnlyList<CrateTarget> BuildableTargets,
    UnbuildableTargets; RustToolchainInfo Toolchain (host); DockerEngineInfo
    Engine (Docker); IReadOnlyList<InstallGuidance> GetGuidance(CrateTarget);
    ToString() multi-line
  BuildReadinessIssue: string Code, Message; bool IsBlocking; CrateTarget?
    Target
  CrateSourceIdentity: string Kind ("local" | "git"), Url, Commit, Branch,
    Tag; bool CloneReused (git: the CloneDirectory already held a clone of
    the same remote, which was fetched instead of cloned; false for a fresh
    clone and for a local source - if (result.Source.CloneReused) ...);
    string ManifestPath, LocalManifestPath, SourceHash, CrateName,
    CrateVersion; IReadOnlyList<string> CrateTypes; const LocalKind, GitKind
  CrateTargetResult: CrateTarget Target; string Rid, Triple;
    CrateTargetOutcome Outcome; IReadOnlyList<string> ArtifactPaths;
    ArtifactProvenance Provenance; IReadOnlyList<InstallGuidance> Guidance;
    string Message; CargoCommandResult CargoResult (in Docker mode its
    paths are container paths); IReadOnlyList<string> VerificationNotes
  WrapperResult: string ProjectPath, ProjectDirectory, EntryClassName (the
    static class with the free functions: <Lib> or <Lib>Library),
    WrapReportPath; IReadOnlyList<string> GeneratedFiles
  CrateBuildResult: bool Success; CrateSourceIdentity Source; BuildMode Mode;
    IReadOnlyList<CrateTargetResult> Targets; string OutputDirectory,
    BuildRecordPath; BuildRecord Record; WrapperResult Wrapper;
    string WrapReportPath (WRAP-REPORT.txt beside the record, its JSON twin
    WRAP-REPORT.json next to it; null when the facade stages did not run);
    CrateReadinessReport Readiness; IReadOnlyList<string> Warnings;
    CrateBuildStage? FailedStage; string FailureMessage; TimeSpan Elapsed

BuildRecord (sealed class) - crate-build-record.json in OutputDirectory
  const string DefaultFileName = "crate-build-record.json"
  string CrateBuilderVersion; DateTimeOffset StartedUtc, FinishedUtc;
  CrateSourceIdentity Source; BuildMode Mode; CrateBuildProfile Profile;
  IList<string> Features; bool NoDefaultFeatures, Offline, Locked;
  string ToolchainDescription; IList<TargetRecord> Targets; string
  WebGpuNativeVersion (null when not linked); FacadeRecord Facade (null when
  the facade stages did not run); string WrapReportFile ("WRAP-REPORT.txt"
  beside the record, or null); WrapperRecord Wrapper (null when none);
  IList<string> Warnings (the failure is listed here too)
  static BuildRecord Load(string path); void Save(string path);
  string ToJson()
  JSON: indented, camelCase, enums as strings, nulls omitted, UTF-8 without
  BOM, "\n" line endings. "source" holds the CrateSourceIdentity: kind, url,
  commit, branch, tag, cloneReused (always written, true or false),
  manifestPath, localManifestPath, sourceHash, crateName, crateVersion,
  crateTypes.
  TargetRecord: string Rid, Triple; CrateTargetOutcome Outcome;
    ArtifactProvenance Provenance; IList<ArtifactRecord> Artifacts; string
    Message
  ArtifactRecord: string Path (relative to OutputDirectory, '/'), Sha256;
    long Length
  WrapperRecord: string AssemblyName, RootNamespace, PackageId,
    EntryClassName (the wrapper's entry class, as WrapperResult), ProjectPath
  FacadeRecord: bool Generated (false = the crate was a cdylib with C
    exports, built as it is); string LibName (the native file's base and the
    library name the wrapper imports); string PackageName (the facade
    crate's package; null when not generated); int ExportCount;
    IList<string> Exports (the C function names); string Reason (why the
    facade stages ran: a wrapper was requested, the crate is not a cdylib,
    FacadeMode.Always, or the Auto fallback for a cdylib that exported
    nothing)

Exceptions (all : CrateBuilderException : RustToolsException; each has the
(string message) and (string message, Exception innerException)
constructors, CrateBuilderException and FacadeGenerationException also
(string message, CrateBuildStage stage, Exception innerException))
  CrateBuilderException        CrateBuildStage Stage
  SourceResolutionException    Stage = ResolvingSource
  StagingException             Stage = Staging
  FacadeGenerationException    Stage = RenderingFacade, or the facade stage
                               given (ExtractingApi, Planning)
  WrapperGenerationException   Stage = Wrapping
  ArtifactVerificationException  Stage = Verifying
  BuildAsync reports these as FailedStage / FailureMessage; they reach you
  as exceptions only from the methods that document them (BuildRecord.Load).


PIPELINE
========
  1. Readiness       the targets this machine can build in the mode (see
                     READINESS); a blocking problem ends the build here,
                     without a record.
  2. ResolvingSource the local crate as it is, or the Git clone (see SOURCES
                     AND STAGING).
  3. Staging         a copy under the scratch folder; everything after this
                     works on the copy.
  4. ExtractingApi, Planning, RenderingFacade - by request.FacadeMode:
                     Auto (default): when a wrapper is requested or the
                     crate does not declare a cdylib. The public API is
                     read, the C mapping is planned (WRAP-REPORT.txt), and
                     for a plain Rust library a C facade crate is generated
                     (see THE GENERATED FACADE). A crate that already is a
                     cdylib with C exports is built as it is (pass-through:
                     its own extern "C" exports are declared in the
                     wrapper). A cdylib built WITHOUT a wrapper skips these
                     stages and is built directly; when verification then
                     finds that it exports nothing, the SAME run goes
                     through these stages, builds the generated facade
                     instead, adds a warning, and the record's
                     Facade.Reason says so.
                     Never: the crate is built as it is, always (a library
                     that exports nothing then fails with "exports no
                     symbols"); not allowed with a Wrapper.
                     Always: these stages always run, and a facade is
                     generated even over a cdylib that has C exports of its
                     own (those are then not passed through).
  5. Building        cargo once per target (host) or one build container for
                     all targets (Docker); the native files land in
                     OutputDirectory (runtimes/<rid>/native/ or Flat).
  6. Verifying       every file is checked ONCE, right after it is built:
                     format and architecture, at least one export, the
                     WebGPU link when requested, the glibc floor for
                     ManyLinux. The notes also name the libraries the file
                     needs, its exports, the highest glibc version it needs
                     (Linux) and the Android API level it was linked for
                     ("linked for Android API level 24 ..."; Android). A rejected file makes its target Failed (the
                     file stays for inspection). Verification notes that
                     start with "WARNING: " are also copied to
                     CrateBuildResult.Warnings and the record.
  7. Wrapping        the wrapper project (only with Wrapper).
  8. Recording       crate-build-record.json (and WRAP-REPORT.txt / .json)
                     in OutputDirectory.
  9. CleaningUp      the clone when CleanUpAfterBuild, then the run's scratch
                     folder unless KeepScratch.


SOURCES AND STAGING
===================
Local: LocalManifestSource takes a Cargo.toml or its folder. It is only
read; the build never writes into it (no target/ appears there).

Git: GitRepositorySource is cloned with CodeBrix.LibGit2 (public HTTPS by
default; CredentialsProvider is passed through for private repositories;
RecurseSubmodules for submodules).
  - CloneDirectory null = <scratch>/runs/<run>/clone/<repository name>,
    deleted with the run folder unless KeepScratch.
  - Commit -> detached HEAD at that commit (an abbreviated SHA works);
    Branch -> a local branch reset to origin/<branch> (it OVERWRITES a local
    branch of that name); Tag -> detached at the tag's commit; none -> the
    clone's default branch.
  - REUSE: a CloneDirectory that already holds a clone of the SAME remote
    (trailing "/" and ".git" ignored, host case-insensitive) is fetched and
    checked out again instead of cloned. With no Commit / Branch / Tag a
    reused clone is checked out DETACHED at the remote default branch's tip.
    Checkouts are safe: local changes a checkout would overwrite fail the
    stage instead of being discarded. CrateBuildResult.Source.CloneReused
    (and "cloneReused" in the record) says whether the clone was reused.
  - Any OTHER non-empty folder (not a repository, a bare one, a clone of
    another remote) is a ResolvingSource failure, and the folder is left
    untouched.
  - CleanUpAfterBuild = true deletes the clone folder in CleaningUp - also
    YOUR CloneDirectory, and also a clone that was reused.
  - ManifestPath is relative to the repository root, without ".." and not
    rooted (a request that breaks this is an ArgumentException from
    Validate).

Staging: the crate's workspace root (cargo's rule: the nearest [workspace]
that does not exclude the crate) is copied to
<run>/stage/<package>/src/; path dependencies OUTSIDE that tree are copied
to <run>/stage/<package>/.codebrix-vendor/<n>-<name>/ and only the STAGED
manifests are rewritten to point at them (comments and formatting kept).
Left out of the copy: .git, cargo's target/ folders, CrateBuilder's own
.codebrix-* folders, the version-control / CI / editor folders at the root
(.github, .gitlab, .vscode, .idea, .devcontainer ...; every other hidden
folder IS copied, e.g. .cargo and .sqlx, because builds read them) and
the scratch folder itself when you placed it inside the crate. A build of a
generated facade also uses the workspace's .cargo/config.toml (passed to
cargo with --config) and the [patch] / [replace] and [profile.*] tables of
its root Cargo.toml (copied into the facade's Cargo.toml), so the crate is
built against the same dependencies and with the same profile settings
(lto, codegen-units, opt-level, per-package overrides ...) as in its own
workspace - in both modes. The one exception: panic is always "unwind" in
the dev and release profiles (a workspace "abort" is replaced, with a comment
in the facade's Cargo.toml), because the facade catches panics at its export
boundary. Git
dependencies are left to cargo: they need the network, or a cargo cache that
already holds them (Offline). The source identity's SourceHash is SHA-256
over every staged file (path, NUL, bytes, NUL, in ordinal path order) - the
same files give the same hash on every OS.


BUILDING
========
Host mode builds with the toolchain installed here, one cargo run per
target: the host's own triple natively, other Linux triples with the GNU
cross gcc, Android with the NDK (API 24, or 33 when LinkToCodeBrixWebGpu),
macOS through the macOS link profile on a machine that is not a Mac (a
DevelopmentBuild), Windows through llvm-mingw (gnullvm).
Docker mode copies the whole staging root to /work of a build container and
runs cargo per target in the built crate's folder below it, each target
with its own contribution variables (extra rustflags extend the Docker
package's per-target flags). Android is linked at the same API level as in
host mode (24, or 33 when LinkToCodeBrixWebGpu).
  LinuxBaseline.RustImage (default) = the official rust image plus the
  Docker package's cross layers, every target, all in ONE container.
  LinuxBaseline.ManyLinux = CrateBuilder's manylinux images, one per Linux
  architecture, ONE image and ONE container per platform; the other six
  targets are NotBuildableInThisMode (the readiness check already says so).
  Each library is checked against the glibc floor of its own image:
      linux-x64      manylinux_2_28_x86_64    glibc 2.28   linux/amd64
      linux-arm64    manylinux_2_28_aarch64   glibc 2.28   linux/arm64
      linux-riscv64  manylinux_2_39_riscv64   glibc 2.39   linux/riscv64
  (quay.io/pypa publishes no older manylinux baseline for riscv64; every
  riscv64 distribution in use is newer than 2.39.) The images are
  digest-pinned and install the pinned Rust with a checksum-verified
  rustup-init.
  PREREQUISITE FOR THE FOREIGN ARCHITECTURES: an image of another
  architecture than the engine's own (linux-arm64 and linux-riscv64 on an
  x86-64 engine) is built and run under qemu-user EMULATION, which needs a
  qemu-user binfmt handler for that architecture on the engine's host
  (/proc/sys/fs/binfmt_misc/qemu-aarch64, /proc/sys/fs/binfmt_misc/
  qemu-riscv64; on Debian `sudo apt install qemu-user-static
  binfmt-support`; Docker Desktop brings its own). The build probes each
  such platform once (CodeBrix.RustTools.Docker's ProbePlatformAsync)
  before it builds; when the probe fails the target is
  NotBuildableInThisMode with a message that names the missing qemu-user
  binfmt handler, what the probe found and the guidance text. Nothing is
  installed for you.
  SLOW: everything in an emulated image runs under qemu-user - the image
  build (rustup and the toolchain) and every cargo build. Expect minutes
  where a native build takes seconds, more for crates with many
  dependencies; RISC-V is slower than ARM64. Built images are reused by
  later builds (until they are removed).
Rules for a GENERATED facade crate: the request's Features /
NoDefaultFeatures / AllFeatures are written into the facade's dependency on
your crate (the facade itself has none), and --locked is never passed (the
facade's Cargo.lock is a copy of your crate's, which has no entry for the
facade itself); Offline still applies. For your own crate (no facade)
Locked and the features are passed to cargo as given.
Only THE crate's dynamic library is collected (not dependencies, build
scripts or proc-macros). The licence gate (LicensePolicy) runs in Enforce
mode and reports a violation as a failed target.

SHARED CARGO TARGET FOLDER (CrateBuilderOptions.CargoTargetDirectory)
---------------------------------------------------------------------
By default every run builds in its own target folder inside the run's
staged copy, deleted with the run, so each build compiles the crate's
dependencies again. Set CargoTargetDirectory to one folder and every cargo
run of the builder uses a sub-folder of it as cargo's target folder
(through CARGO_TARGET_DIR, which wins over a CARGO_TARGET_DIR of your own
and over build.target-dir in a cargo configuration file):
    <folder>/host/                     host mode: the crate and a generated
                                       facade (shared, so the facade build
                                       reuses the crate's dependency builds)
    <folder>/docker/                   Docker mode in the rust image: the
                                       same, bind-mounted into the build
                                       container
    <folder>/docker/manylinux-<arch>/  Docker mode with LinuxBaseline.
                                       ManyLinux, one per image (x86_64,
                                       aarch64, riscv64): each image builds
                                       its own architecture natively into
                                       release/, so cargo's triple folders
                                       cannot keep them apart
    <folder>/api-extractor/<hash>/     the API extractor tool, host build
    <folder>/api-extractor-docker/<hash>/  the tool built in a container
Host and Docker builds never share a folder (another toolchain, other
paths and another C library: a build script built for one must not be run
by the other). Each build's native file is copied to OutputDirectory as
soon as cargo finishes, so a later build replacing it in the shared folder
does no harm. Several builders (or processes) may share the folder: cargo
locks it while it builds. CrateBuilder creates the folder when needed and
never deletes it.
Docker mode: the docker/ (docker/manylinux-<arch>/, api-extractor-docker/) folder is BIND-MOUNTED
at /codebrix-cargo-target in the container, so the folder must be on the
machine the Docker engine runs on (not with a remote tcp:// or ssh://
engine) and shared with the engine (Docker Desktop file sharing). A Linux
engine writes the files as root; after the container's last cargo run they
are given the owner and group of the mounted folder (the user who created
it), so you can delete them. If that fails, the result has a warning
naming the folder.


WRAPPLAN (what the wrapper covers)
==================================
Without a WrapPlan everything mappable is wrapped. Patterns are matched
against an item's Rust paths with and without "crate::" ('*' = any run of
characters): "Encoder", "Encoder::*", "geom::*", "*::to_string".
  IncludeTypes / IncludeFunctions   empty = all; otherwise only matches
  ExcludeTypes / ExcludeFunctions   applied after the includes; a type left
                                    out takes its methods with it
  Renames                           exact Rust path -> plain C# identifier:
                                    types, functions, "Type::field",
                                    "Enum::Variant" (managed names only)
  Overrides                         a FacadeOverride replaces the generated
                                    export, or the skipped item, with the
                                    same RustPath: RustSource is pasted into
                                    the facade's `pub mod overrides` (with
                                    `use super::*;` and the facade's support
                                    items: Buffer, HandleBox, the STATUS_*
                                    codes), CSharpSource into the wrapper
                                    (Overrides/<Name>.cs) verbatim
  AdditionalFacadeFiles             your own .rs files, copied into the
                                    facade's src/ and declared
                                    `pub mod <file name>;` (the name must be
                                    a Rust module name and not lib, main,
                                    facade_support, overrides, wgpu_ffi, up
                                    or upstream)
  Notes                             printed at the top of WRAP-REPORT.txt
WRAP-REPORT.txt lists every exported function with its C# signature, the
constants copied into C#, every item NOT wrapped with the reason (generics,
impl Trait, closures, trait objects, iterators, mutable borrowed returns,
Rc/Arc, data-carrying enums no wrapped function uses, async, types from
other crates, types that borrow data for a lifetime, cfg for none of the
targets, a cargo feature the build does not enable ...) and a ready-to-fill
override stub, the objects you must dispose, and units / conventions lifted
from the doc comments. WRAP-REPORT.json holds the same plan as data.
Features: the planner resolves the crate's features as cargo does for this
build (Features, plus "default" unless NoDefaultFeatures, everything with
AllFeatures, then what those enable); an item behind a feature that is OFF
is listed as not wrapped, and adding the feature to Features wraps it. A
#[non_exhaustive] struct is never built from its fields (it is wrapped as an
object), and an error type whose thiserror-style derive writes Display from
#[error("...")] messages gives its Display text as the exception message.

What is mapped automatically (tier 1): integer and float primitives, bool,
C-like enums, Copy structs of mappable fields (by value), fixed arrays of
primitives, any other non-generic struct (as an opaque object with its
methods, Default and Clone), &str / String, slices and Vec of primitives,
Option, Result (the error becomes an exception, Box<dyn Error> included),
and tuples as results. On top of that, these general rules apply (each one
only copies or converts, so it never changes what the Rust code means):
  - Public fields of an object: every pub field whose type crosses C gets a
    getter (Get<Field>(); it returns a COPY of the field, an object field
    only when its type is Clone) and a setter (Set<Field>(value)), unless
    the type already has a method of that name. When EVERY field is pub,
    crosses C and the struct is not #[non_exhaustive], it also gets a
    constructor from its fields (the C# constructor, or the static
    FromFields when the crate has its own `new`). A struct with neither
    methods nor such a way to make one is still listed as not wrapped.
    WrapperOptions.GenerateFieldAccessors = false leaves out the getters
    and setters (only them: the constructor from the fields stays, and so
    do Variant() and the variant constructors below); an object that only
    its accessors made useful is then listed as not wrapped, the SUMMARY of
    WRAP-REPORT.txt says how many accessors were left out ("Field
    accessors: off - N ...", "fieldAccessorsOmitted" in WRAP-REPORT.json)
    and the wrapper's AGENT-README.txt says they are not there.
  - Results that borrow (&T, &str, &[T], Option<&T>, from self, from the
    arguments or 'static) are returned as an owned COPY: values and
    strings always, objects only when their type is Clone. A mutable
    reference (&mut T) is never copied (a copy would drop the caller's
    change).
  - Paths and OS strings: &Path / PathBuf / &OsStr / OsString arguments are
    .NET strings (UTF-8); a PathBuf / OsString / &Path result is a string,
    and one that is not valid UTF-8 fails with -3 and a message (never a
    lossy conversion). CString / CStr are not mapped.
  - Text-bounded parameters: `impl AsRef<str>`, `impl AsRef<Path>`,
    `impl Into<String>` (and AsRef<OsStr>, Into<PathBuf>, ToString), and a
    generic type parameter with exactly one such bound and no where clause
    (`fn f<P: AsRef<Path>>(p: P)`), take a string; AsRef<[u8]> takes a byte
    span. A type parameter that also appears in the result stays unwrapped.
  - Optional text: Option<&str> / Option<String> arguments are string
    (null = None).
  - Enums whose variants carry data: an opaque object (like any non-Copy
    struct) when a wrapped function makes, takes or returns one; it gets
    its methods, Default / Clone, a generated Variant() (the variant's name
    as a string) and one static constructor per variant whose fields cross
    C (Value::Scalar(f64) -> Value.Scalar(double); object fields only when
    Clone; never for a #[non_exhaustive] variant). Its data is never laid
    out in C. Option<Enum> fields and results work for C-like enums that
    are Clone.
  - Lists: &[T] / Vec<T> arguments and Vec<T> results where T is a
    by-value struct, a C-like enum (Clone), bool or a fixed array of numbers
    are ReadOnlySpan<T> / T[] (copied element by element); lists of strings
    (&[&str], &[String], Vec<String>) are ReadOnlySpan<string> / string[];
    lists of objects are ReadOnlySpan<Obj> in (each one is CLONED into the
    Rust list, so only Clone types) and Obj[] out (each element its own
    object to dispose). Optional object fields (Option<Obj>, Clone) get a
    getter and a setter taking null for None.
  - Constants: a pub const (or non-mut static) of a number, bool or &str
    type whose value is a plain literal becomes a C# `const` of the entry
    class (MAX_SIZE -> MaxSize; usize / isize as nuint / nint when the value
    fits 32 bits). Nothing crosses C for it. A constant whose value is an
    expression is listed as not wrapped (CrateBuilder does not run rustc's
    constant evaluation), and so is an associated constant (`impl T {
    pub const X: u32 = 1; }`, reported as "T::X"): only free constants are
    copied. WrapPlan.ExcludeFunctions and Renames apply to constants by their
    Rust path.


THE GENERATED FACADE (C conventions)
====================================
For a plain Rust library CrateBuilder writes a cdylib crate in
<stage>/.codebrix-facade/<lib>_facade/ (Cargo.toml, a copy of Cargo.lock,
src/lib.rs, <lib>.h, build.rs when WebGPU-linked, FACADE-README.txt). Its
[lib] name is your crate's library name, so the native file is
lib<lib>.so / lib<lib>.dylib / <lib>.dll, and every export is prefixed
"<lib>_". You do not call these functions yourself - the wrapper does - but
they are a plain C API (the header documents it) for any other language:
  - Status: every export except *_destroy and *_free_* returns int32_t:
        0  OK                 results are written to the out-parameters
       -1  NULL ARGUMENT      a required pointer was NULL
       -2  INVALID ARGUMENT   not UTF-8, a bool that is not 0/1, an enum
                              value that is not in the enum, a bad length
                              or alignment, the same object twice where one
                              is changed or used up
       -3  UPSTREAM ERROR     the Rust function returned Err(e); the message
                              is e's Display (or Debug, or its type name)
       -4  PANIC              the Rust code panicked (caught); the objects of
                              that call are BROKEN: they can only be
                              destroyed. Rust's default panic hook still
                              prints its "thread ... panicked at ..." line to
                              the process's standard error before the panic
                              is caught: the facade does not install a quiet
                              hook, because the hook is process-wide and would
                              also silence every other Rust library in the
                              process. The message itself comes back through
                              last_error_message. A consumer that wants a
                              quiet stderr installs its own hook
                              (std::panic::set_hook) in Rust code it owns.
       -5  HANDLE CONSUMED    the object was used up by a consuming method
  - Out-parameters (out, out_present, out0, out1 ...) are the last
    parameters and are written ONLY on status 0.
  - Last error: int32_t <lib>_last_error_message(uint8_t* buf, size_t cap,
    size_t* written) - the calling thread's message (UTF-8, NOT
    NUL-terminated). Ask for the length with (NULL, 0, &n) (returns 0 with
    n = 0 when there is no message, -2 when n > 0), then call with a buffer
    of n bytes (returns 0). written NULL -> -1. The next status-returning
    call clears the message.
  - Buffers: typedef struct { uint8_t* data; size_t len; size_t cap; }
    <lib>_buffer. len and cap count BYTES; a Vec of numbers is handed out as
    their native-endian bytes (element count = len / sizeof(T)); an owned
    String is its UTF-8 bytes; data is NULL for an empty value. Release
    each buffer exactly once, BY VALUE: <lib>_free_buffer(buffer) /
    <lib>_free_string(buffer).
  - Strings in: pointer + byte length, UTF-8, no NUL needed; slices in:
    pointer + ELEMENT count. A pointer may be NULL only when its length is 0.
    A slice or Vec of plain values (structs, enums, bool, arrays) passes
    its elements in their C form (struct, int32_t, one byte, array struct);
    an element that is not valid (an enum value outside the enum, a bool
    other than 0/1) is -2. An owned Vec of such values is a buffer of those
    C elements. A list of strings in is an array of <lib>_str_ref structs
    { const uint8_t* data; size_t len; } + count; a list of objects in is an
    array of object pointers + count (each is cloned; a NULL or used-up one
    fails like a single object argument). An owned Vec of objects is a
    buffer of new object pointers (destroy each); an owned Vec<String> is a
    buffer of <lib>_buffer structs (free each with free_string, then the
    outer one with free_buffer). An optional string is a present flag +
    pointer + length.
  - bool: C bool in the header, one byte at the ABI, 0 or 1 only.
  - Enums: int32_t at the boundary with named constants (<LIB>_<ENUM>_<V>).
  - Structs (mirrors of Copy structs): C structs passed and returned BY
    VALUE; bool fields are one byte, enum fields int32_t, nested structs by
    value, fixed arrays as a struct { T values[N]...; }. Their size, alignment
    and field offsets are asserted at compile time.
  - Objects: opaque pointers created by their constructor functions;
    void <type>_destroy(<type>* handle) exactly once (NULL is ignored).
  - Order of work in every export: clear the last error, null-check the
    outs, convert and validate every argument, then call - so a rejected
    call never uses up an object.
  - Objects are not synchronised: do not use one from two threads at the
    same time (the wrapper's classes lock for you).
  - Every C name (types, enum constants, functions, the fixed names
    <lib>_buffer, <lib>_last_error_message, <lib>_free_buffer,
    <lib>_free_string and the status macros) is unique: a Rust item whose
    name would collide gets a "_2" suffix (the report shows the names).
  - <lib>.h uses #pragma once, includes only <stdbool.h>, <stddef.h> and
    <stdint.h>, wraps its declarations in extern "C" for C++, and compiles
    as strict C11 (-std=c11 -Wall -Wextra -Werror -pedantic-errors).


THE WRAPPER PROJECT
===================
Folder: WrapperOptions.OutputDirectory, else a sibling of the request's
OutputDirectory named <AssemblyName> (OutputDirectory /work/geo/native ->
/work/geo/Acme.Geo/). Files:
  <AssemblyName>.csproj            net10.0, nullable off, unsafe allowed,
                                   XML docs; runtimes/** copied to your
                                   output (and packed)
  <Lib>.cs                         static class with the free functions
  <Lib>Exception.cs                the exception of a failed call (int
                                   Status = the C status; Message = the
                                   Rust error message)
  <Type>.cs                        one IDisposable class per Rust object
  Mirrors/, Enums/, Arrays/        record structs, enums, [InlineArray]
                                   structs for fixed arrays
  Overrides/                       your CSharpSource files
  Native/                          the loader and the [LibraryImport]
                                   declarations (internal)
  runtimes/<rid>/native/           the built native files
  README.md, AGENT-README.txt, THIRD-PARTY-NOTICES.txt, licenses/,
  WRAP-REPORT.txt (+ .json)
  Tests/<Lib>SmokeTests.cs         only with GenerateSmokeTests (xUnit; NOT
                                   compiled into the wrapper - copy it into
                                   a test project)
  generated-files.json             what the writer generated
<Lib> = the crate's library name in PascalCase ("datacrate" -> Datacrate),
with "Library" appended when that equals a type name or the last segment of
RootNamespace (RootNamespace "Acme.Datacrate" -> class DatacrateLibrary).
The wrapper's README.md and AGENT-README.txt show the resulting class in
their first C# example (and say why when the suffix was added), and
WrapperResult.EntryClassName / the record's Wrapper.EntryClassName name it.
Managed shapes: objects = sealed partial classes with methods (a
constructor for the Rust `new` or for the public fields, static factories
for other constructors, Get<Field>() / Set<Field>(value) for public
fields), IDisposable, a lock per object, the native pointer held by an
internal SafeHandle (destroyed exactly once: by Dispose, or by the
finalizer when an object is never disposed; the handle is ref-counted for
the length of every call, so Dispose on another thread never frees it
mid-call), ObjectDisposedException after Dispose, InvalidOperationException
after a consuming (self) method; Copy structs =
readonly record structs with a field constructor; C-like enums = C# enums;
strings (and paths) = string; slices = ReadOnlySpan<T> / Span<T>; owned
Vec<T> = T[]; Option = T? or null; Result = the value, or <Lib>Exception;
tuples = value tuples; usize / isize stay nuint / nint; literal constants =
`public const` members of <Lib>. A generated type whose name a member also
uses (fn vec2() -> Vec2 becomes the method Vec2) is written fully qualified
(global::<RootNamespace>.Vec2) inside the wrapper. XML docs come from the
Rust doc comments; a result that is a copy of a borrowed value says so.
Loader: the native file is looked up in runtimes/<rid>/native/ under
AppContext.BaseDirectory, then beside the application, then by the system's
own search; when none is found a DllNotFoundException names the expected
path and the RIDs the wrapper includes. When the crate links CodeBrix WebGPU
the loader calls CodeBrixWebGpuNative.EnsureLoaded() FIRST and the csproj
references CodeBrix.WebGpu.Native.MitLicenseForever.
Packaging null = a plain class library (IsPackable false); with Packaging
the csproj is packable with the date-stamped version (1.<years>.<day>.
<minute>) and the metadata given; pack it with `dotnet pack`.
Regenerating writes only files whose bytes changed and removes only files
the previous generated-files.json listed; your own files in the folder are
kept. A NON-EMPTY folder without generated-files.json is refused (Wrapping
fails and the folder is not touched).


WEBGPU LINKING (LinkToCodeBrixWebGpu)
=====================================
For a crate that calls the CodeBrix WebGPU engine (codebrix_wgpu) directly:
CrateBuilder stages the CodeBrix.WebGpu.Native package's include/ files and
the target's engine binary into <stage>/.codebrix-wgpu/ (include/ and
<rid>/), sets CODEBRIX_WGPU_INCLUDE_DIR and CODEBRIX_WGPU_LIB_DIR for cargo
and adds "-L native=<that folder>" (not for Windows, raw-dylib needs none).
The crate itself follows the package's recipe:
    // src/lib.rs
    #[allow(non_camel_case_types, non_snake_case, non_upper_case_globals, dead_code)]
    mod wgpu_ffi {
        include!(concat!(env!("CODEBRIX_WGPU_INCLUDE_DIR"), "/codebrix_wgpu_procs.rs"));
    }
    // build.rs
    fn main() {
        println!("cargo::rerun-if-env-changed=CODEBRIX_WGPU_INCLUDE_DIR");
        println!("cargo::rerun-if-env-changed=CODEBRIX_WGPU_LIB_DIR");
        println!("cargo::rustc-check-cfg=cfg(codebrix_wgpu_direct)");
        println!("cargo::rustc-cfg=codebrix_wgpu_direct");
        if std::env::var_os("CODEBRIX_WGPU_INCLUDE_DIR").is_none() {
            panic!("CODEBRIX_WGPU_INCLUDE_DIR is not set: it must name the include/ folder of the CodeBrix.WebGpu.Native package");
        }
        if let Some(dir) = std::env::var_os("CODEBRIX_WGPU_LIB_DIR") {
            println!("cargo::rustc-link-search=native={}", dir.to_string_lossy());
        }
    }
A generated facade gets these lines automatically. After the build the
verifier checks that the file links the engine BY NAME (libcodebrix_wgpu.so
/ @rpath/libcodebrix_wgpu.dylib / codebrix_wgpu.dll) and bundles no WebGPU
engine of its own (no exported wgpu* symbol). A file that calls no engine
function at all has no such link (the linker drops an unused library): it
passes with the warning "... does not reference the engine ...". The record
holds the Native package version used (WebGpuNativeVersion); Android builds
are linked at API level 33 in both modes (the engine's own floor). At run time the engine must be loaded
before the crate: the wrapper's loader calls
CodeBrixWebGpuNative.EnsureLoaded() first.


READINESS
=========
Host mode (CodeBrix.RustTools detection, from the local crate's folder when
there is one so its rust-toolchain.toml applies):
  - BLOCKING: cargo, rustc or the C toolchain missing (and a
    rust-toolchain.toml channel that is not installed). Guidance: install
    Rust / the C toolchain.
  - The host's own triple: buildable.
  - Another Linux triple: its standard library (rustup target add) and the
    GNU cross gcc and ar on PATH (Debian: gcc-<arch>-linux-gnu ...).
  - Android: its standard library and an Android NDK (ANDROID_NDK_HOME or
    ANDROID_HOME).
  - macOS: its standard library; on a host that is not a Mac also the macOS
    link profile's sysroot (CrateBuilderOptions.MacOSSysrootDirectory, a
    folder exported from a CodeBrix.RustTools.Docker image with the macOS
    layer).
  - Windows: the gnullvm standard library and llvm-mingw's
    <machine>-w64-mingw32-clang (CrateBuilderOptions.LlvmMingwDirectory,
    else LLVM_MINGW_HOME, else PATH), checked through CodeBrix.RustTools'
    ToolchainRequirements.ForWindowsGnullvm / LlvmMingwLocator; a missing
    one is windows.llvm-mingw-missing with InstallGuidance topic
    InstallLlvmMingw.
Docker mode (the Docker workbench's engine check):
  - BLOCKING: the engine is unreachable, refuses access or runs Windows
    containers. DockerGuidance says how to fix it.
  - Android targets need an x86-64 engine.
  - Any target that needs a derived image (everything except the engine's
    own Linux architecture on the rust image) needs the docker CLI.
  - LinuxBaseline.ManyLinux: the three Linux targets (docker.manylinux-limit
    for the others). A Linux target whose manylinux image is not the
    engine's own architecture gets the WARNING docker.manylinux-emulated
    (qemu-user emulation, its binfmt prerequisite, the slowness); the
    readiness check does not run the platform probe - the build does.
  - Otherwise every target is buildable: the Docker package supplies the
    cross layers.
When no requested target is buildable, the report is not ready
(targets.none-buildable).
Issue codes: toolchain.<kind> and target.<kind> (the CodeBrix.RustTools
ReadinessIssueKind in kebab case, e.g. target.target-missing,
target.linux-cross-tool-missing, target.android-ndk-missing),
macos.link-profile-unavailable, windows.llvm-mingw-missing,
docker.engine-unavailable, docker.android-needs-x64-engine,
docker.manylinux-limit, docker.manylinux-emulated (warning),
docker.cli-missing, targets.none-buildable.


TARGETS: WHAT EACH ONE NEEDS
============================
One request uses ONE mode. What a target needs besides the mode's basics
(host: cargo, rustc and a C toolchain; Docker: a reachable engine in
Linux-container mode). "std" = the target's standard library, added with
`rustup target add <triple>`. The readiness check reports exactly these
items, with install guidance; nothing is installed for you.

  linux-x64      Host: on an x86-64 Linux host nothing more; elsewhere std
                 + the GNU cross gcc (x86_64-linux-gnu-gcc).
                 Docker: nothing more on an x86-64 engine (ManyLinux too:
                 glibc 2.28); else a derived image (docker CLI), and for
                 ManyLinux the qemu-user emulation of linux/amd64.
  linux-arm64    Host: on an ARM64 Linux host nothing more; elsewhere std
                 + aarch64-linux-gnu-gcc and its ar on PATH (Debian:
                 gcc-aarch64-linux-gnu).
                 Docker: a derived image (docker CLI) unless the engine is
                 ARM64. ManyLinux (glibc 2.28): the docker CLI and, on an
                 engine that is not ARM64, the qemu-user binfmt handler for
                 aarch64 (emulated, slow).
  linux-riscv64  Host: on a RISC-V 64 Linux host nothing more; elsewhere
                 std + riscv64-linux-gnu-gcc (Debian: gcc-riscv64-linux-gnu).
                 Docker: a derived image (docker CLI). ManyLinux (glibc
                 2.39): the docker CLI and the qemu-user binfmt handler for
                 riscv64 (emulated, slower still).
  android-x64,   Host: std + an Android NDK (ANDROID_NDK_HOME or
  android-arm64  ANDROID_HOME); linked at API 24, or 33 when WebGPU-linked.
                 Docker: an x86-64 engine and the docker CLI (the derived
                 image downloads the NDK); same API levels.
  win-x64,       Host: the gnullvm std (x86_64- / aarch64-pc-windows-gnullvm)
  win-arm64      + llvm-mingw's <machine>-w64-mingw32-clang
                 (CrateBuilderOptions.LlvmMingwDirectory, LLVM_MINGW_HOME,
                 PATH). Universal C Runtime, never MSVC.
                 Docker: the docker CLI (the derived image holds llvm-mingw).
  osx-x64,       Host on a Mac: std; the files are Distributable. Host
  osx-arm64      elsewhere: std + the macOS link profile's sysroot
                 (CrateBuilderOptions.MacOSSysrootDirectory, exported with
                 DockerRustWorkbench.ExportMacOSSysrootAsync).
                 Docker: the docker CLI (the derived image has the macOS
                 layer). Built anywhere but on a Mac = DevelopmentBuild:
                 development and testing only, never distributed. Pure-Rust
                 dependency trees only (a crate that compiles C needs Apple's
                 headers, so a Mac).
Every target except macOS-not-on-a-Mac is Distributable.

HOST OR DOCKER
--------------
  Host (BuildMode.Host, the default): fastest, uses the machine's
  toolchain and caches; the machine needs the tools above per target; a
  rust-toolchain.toml in the crate's folder applies.
  Docker (BuildMode.Docker): needs no Rust tools on the machine, builds
  every target from one Linux engine in ONE container, and gives
  reproducible images (official rust image + the Docker package's cross
  layers); the first build of each derived image takes minutes, later ones
  come from the engine's cache. LinuxBaseline.ManyLinux (Docker only)
  builds the three Linux targets against the old glibc floors of the
  manylinux standard (2.28; 2.39 for riscv64), for Linux files that must
  load on old distributions - the foreign architectures under qemu-user
  emulation (prerequisite and slowness above).
  Both modes give the same triples, file names, layout, record and wrapper.


COMPLETE EXAMPLES
=================

Example 1 - Can this machine build my crate, and for which RIDs?
----------------------------------------------------------------
    using System;
    using System.Linq;
    using CodeBrix.RustTools.CrateBuilder;

    using var builder = new CrateBuilder();
    var request = new CrateBuildRequest
    {
        Source = new LocalManifestSource("/src/my-crate/Cargo.toml"),
        Targets = CrateTargets.All.ToList(),
        OutputDirectory = "/src/my-crate-native",
    };
    CrateReadinessReport report = await builder.CheckReadinessAsync(request);
    Console.WriteLine(report);              // buildable / not buildable, issues
    foreach (var target in report.UnbuildableTargets)
    {
        foreach (var step in report.GetGuidance(target))
        {
            Console.WriteLine(step);        // show it; never run it
        }
    }

Example 2 - Build in Docker and read the outcome per target
----------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using CodeBrix.RustTools.CrateBuilder;

    using var builder = new CrateBuilder();
    builder.Progress += (_, p) => Console.WriteLine(p);
    CrateBuildResult result = await builder.BuildAsync(new CrateBuildRequest
    {
        Source = new GitRepositorySource("https://github.com/owner/repo.git")
        {
            Tag = "v1.2.0",
            CleanUpAfterBuild = true,
        },
        Mode = BuildMode.Docker,
        Targets = new List<CrateTarget>
        {
            CrateTarget.LinuxX64, CrateTarget.WinX64, CrateTarget.AndroidArm64,
        },
        OutputDirectory = "/out/native",
        Wrapper = new WrapperOptions
        {
            AssemblyName = "MyCompany.RepoBindings",
            RootNamespace = "MyCompany.RepoBindings",
        },
    });
    Console.WriteLine(result);              // outcome, failure, one line per RID
    if (result.FailedStage.HasValue)
    {
        Console.WriteLine($"{result.FailedStage}: {result.FailureMessage}");
    }

    Console.WriteLine(result.BuildRecordPath);   // crate-build-record.json


Example 3 - Wrap part of a local crate, packable, and call it
--------------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using CodeBrix.RustTools.CrateBuilder;

    using var builder = new CrateBuilder();
    var result = await builder.BuildAsync(new CrateBuildRequest
    {
        Source = new LocalManifestSource("/src/geo/Cargo.toml"),
        Targets = { CrateTarget.LinuxX64, CrateTarget.LinuxArm64 },
        OutputDirectory = "/out/geo/native",
        Wrapper = new WrapperOptions
        {
            AssemblyName = "Acme.Geo",
            RootNamespace = "Acme.Geo",
            Packaging = new WrapperPackaging { PackageId = "Acme.Geo", Authors = "Acme" },
            Plan = new WrapPlan
            {
                IncludeTypes = { "Polygon", "Point" },
                ExcludeFunctions = { "*::debug_dump" },
                Renames = { ["crate::Polygon::area"] = "Area" },
            },
        },
    });
    if (!result.Success)
    {
        Console.WriteLine(result);              // which stage / which RID, and why
        return;
    }

    Console.WriteLine(result.WrapReportPath);  // read it: what was skipped and why
    // dotnet pack /out/geo/Acme.Geo -c Release, or reference the .csproj

    // In the consuming app (namespace Acme.Geo):
    //   using var polygon = new Polygon();     // an object: dispose it
    //   double a = polygon.Area();


MINIMUM VIABLE PROJECT TEMPLATE
===============================
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference
            Include="CodeBrix.RustTools.CrateBuilder.MitLicenseForever"
            Version="*" />
      </ItemGroup>
    </Project>


COMMON PITFALLS TO AVOID
========================
- Do not expect BuildAsync to throw for a failed stage: read FailedStage
  and FailureMessage. Only an invalid request, a disposed builder and YOUR
  cancellation throw.
- Do not run guidance commands automatically; show them to a person.
- Success is false when any requested target is NotBuildableInThisMode,
  even though the others were built: check each CrateTargetResult.
- Flat layout is for one target only; ByTarget is not supported.
- macOS files built in Docker, or on a host that is not a Mac, are
  DevelopmentBuild (development and testing only; never distribute them).
- Read WRAP-REPORT.txt before calling the wrapper: items that are not
  mapped automatically (generics, callbacks, iterators ...) are listed there
  with an override stub, not silently dropped.
- Dispose every wrapper object (using). After a panic (-4) an object is
  broken and only Dispose is allowed; after a consuming method the object
  throws InvalidOperationException.
- A getter of an object field, and a method whose Rust result is a
  reference, return a COPY: calling a setter on that copy does not change
  the object it came from (set the field on the parent with its own setter
  instead).
- A wrapper folder must be empty or one the writer created
  (generated-files.json); point WrapperOptions.OutputDirectory elsewhere
  rather than at a folder of your own files.
- Git dependencies of the crate need the network (or Offline with a warm
  cargo cache); Offline with an empty cache fails in Building.
- An Android library loads on the API level it was linked for and later
  (24, or 33 when WebGPU-linked; the verification notes name it): set your
  app's minSdk at least that high.
- Do not put KeepScratch on for every build: run folders are not reused and
  hold a full copy of the crate. The API extractor's build is cached under
  <scratch>/api-extractor/ and reused by every build. To reuse the crate's
  compiled dependencies between builds, set CargoTargetDirectory instead.
- A cdylib built without a Wrapper is built as it is first; only when it
  exports nothing does FacadeMode.Auto build a facade (a second pass in the
  same run, with a warning). Set FacadeMode.Always to skip the first build,
  or Never to keep the crate exactly as it is.
- LinuxBaseline.ManyLinux for linux-arm64 / linux-riscv64 on an x86-64
  engine runs everything under qemu-user emulation: register the engine
  host's qemu-user binfmt handlers first (the target is otherwise
  NotBuildableInThisMode with the guidance in its message), and budget
  minutes per build - a crate with many dependencies takes much longer
  than the small samples. For linux-arm64 / linux-riscv64 files that may
  need a newer glibc, LinuxBaseline.RustImage cross-compiles them natively
  and fast.
- The riscv64 manylinux floor is glibc 2.39, not 2.28: there is no older
  manylinux baseline for riscv64. Read each target's floor in its
  verification notes.


WHAT THIS PACKAGE DOES NOT DO
=============================
- Install, update or configure Rust, targets, cross toolchains, the NDK,
  llvm-mingw or Docker.
- Mix host and container builds in one request.
- Pack NuGet packages itself (the wrapper project is packable when
  Packaging is given; packing is yours).
- Sign or notarise macOS binaries.
- Wrap generic, trait-object, closure, iterator, async or lifetime-bound
  APIs automatically (tier 1 only; use a FacadeOverride). The reasons, as
  the wrap report also gives them: a generic needs one concrete type the
  crate does not name; a trait object or closure has no fixed C layout; an
  async function needs an executor CrateBuilder cannot choose for you; a
  type that borrows for a lifetime cannot be kept by .NET for as long as it
  likes; Rc / Arc would have to be copied, which loses the sharing; the
  data of an enum variant would need a tagged union per variant (such an
  enum is an object instead, see tier 1); a type
  from another crate cannot be looked into (only this crate's source is
  read); a mutable reference result exists to be changed in place.
- Set up emulation: the ManyLinux images of linux-arm64 / linux-riscv64 on
  an x86-64 engine need the engine host's qemu-user binfmt handlers, which
  CrateBuilder only probes for and explains.


WORKING EXAMPLES ON GITHUB
==========================
https://github.com/ellisnet/CodeBrix.RustTools/tree/main/
    tests/CodeBrix.RustTools.CrateBuilder.Tests
  Targets ........................ Targets/CrateTargetsTests.cs
  Requests ....................... Requests/CrateBuildRequestTests.cs,
                                   Requests/WrapperOptionsTests.cs
  Build record ................... Records/BuildRecordTests.cs
  Stage order and failures ....... Internal/Pipeline/CrateBuilderPipelineTests.cs
  End to end (URL / crate in,
    wrapper and console app out) . EndToEnd/HostEndToEndTests.cs,
                                   EndToEnd/GitEndToEndTests.cs,
                                   EndToEnd/DockerEndToEndTests.cs
  Real-world crate shapes ........ EndToEnd/CrateShapesEndToEndTests.cs,
                                   EndToEnd/DockerWorkspaceEndToEndTests.cs
  Default-wrap rules (fields,
    copies, paths, lists,
    constants) ................... EndToEnd/DefaultWrapRulesEndToEndTests.cs,
                                   Facade/Planning/ConstantLiteralsTests.cs
  Docker mode (WebGPU-linked for
    Linux / Windows / Android,
    Android API levels) .......... EndToEnd/DockerEndToEndTests.cs
  Sources (clone, reuse,
    submodules) .................. Sources/SourceResolverTests.cs,
                                   Sources/GitRemoteUrlTests.cs
  Staging ........................ Staging/WorkspaceStagerTests.cs,
                                   Staging/WorkspaceRootFinderTests.cs,
                                   Staging/ManifestPathRewriterTests.cs
  Build drivers .................. Building/HostBuildDriverTests.cs,
                                   Building/DockerBuildDriverTests.cs
  ManyLinux (three images, glibc
    floors, linux-arm64 and
    linux-riscv64 under qemu) .... Building/ManyLinux/ManyLinuxImagesTests.cs,
                                   Building/TargetMatrixTests.cs,
                                   EndToEnd/DockerEndToEndTests.cs
  Shared cargo target folder ..... Building/SharedCargoTargetTests.cs,
                                   EndToEnd/HostEndToEndTests.cs,
                                   EndToEnd/DockerEndToEndTests.cs
  Verification ................... Building/Verification/
                                   DefaultArtifactVerifierTests.cs,
                                   NativeArtifactReaderTests.cs
  WebGPU linking ................. WebGpu/WebGpuLinkLiveTests.cs,
                                   WebGpu/WebGpuLinkVerifierTests.cs
  WrapPlan and the report ........ Facade/Planning/MappingPlannerTests.cs,
                                   Facade/Planning/FeatureResolverTests.cs,
                                   Facade/Planning/WrapReportWriterTests.cs
  Facade C conventions and the
    header compiled as C11 ....... Facade/Rendering/FacadeRendererLiveTests.cs
  Wrapper ........................ Wrapper/WrapperProjectWriterTests.cs,
                                   Wrapper/WrapperGenerationTests.cs,
                                   Wrapper/WrapperRealPlanTests.cs,
                                   Wrapper/WrapperLiveTests.cs
  Readiness ...................... Internal/Readiness/CrateReadinessTests.cs
  Exceptions ..................... CrateBuilderExceptionTests.cs


QUICK REFERENCE CARD
====================
  Builder           using var b = new CrateBuilder(options /* or null */);
  Ready?            var r = await b.CheckReadinessAsync(request);
  Build             var res = await b.BuildAsync(request, progress, ct);
  Local crate       Source = new LocalManifestSource("/path/Cargo.toml")
  Git crate         Source = new GitRepositorySource(url, "crates/x/Cargo.toml")
                        { Commit = "<sha>" }
  Container         Mode = BuildMode.Docker
  Every RID         Targets = CrateTargets.All.ToList()
  RID facts         CrateTargets.Rid / Triple / NativeFileName / FromRid
  Wrapper           Wrapper = new WrapperOptions { AssemblyName = "A",
                        RootNamespace = "A" }
  Outcome           res.Success, res.FailedStage, res.Targets[i].Outcome
  Record            BuildRecord.Load(res.BuildRecordPath)
  Wrapper project   res.Wrapper.ProjectPath (reference it, or dotnet pack)
  Wrap report       res.WrapReportPath (WRAP-REPORT.txt)
  Select / rename   Wrapper.Plan = new WrapPlan { IncludeTypes = { "X" } }
  WebGPU crate      LinkToCodeBrixWebGpu = true
  Reuse deps        new CrateBuilderOptions { CargoTargetDirectory = dir }
  No field getters  Wrapper.GenerateFieldAccessors = false
  Clone reused?     res.Source.CloneReused

Target: .NET 10 or later
License: MIT

================================================================================
END OF AGENT-README
================================================================================
