================================================================================
AGENT-README: CodeBrix.RustTools.Docker
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.RustTools.Docker.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.RustTools.Docker is a .NET 10 library for working with Rust code
INSIDE A LINUX DOCKER CONTAINER. The machine it runs on needs a reachable
Docker engine (Docker Desktop on Windows and macOS, Docker Engine on Linux)
and NO Rust tools at all: rustup, cargo and rustc live in the container.

It NEVER installs, starts or configures Docker. When the engine cannot be
used it returns DockerGuidance objects (title, explanation, ordered
commands, documentation link) written for the host operating system, for
YOUR application to show to the user.

Feature areas:
  - Engine check: is the engine reachable, does it run Linux containers,
    which version and architecture is it, is the docker CLI on PATH - plus
    per-OS guidance (install Docker Desktop / Docker Engine, start it, add
    the user to the docker group, enable WSL 2, switch to Linux containers).
  - Build images: the official `rust` image by default, optionally with
    generated Dockerfile layers - extra Debian packages, clippy and rustfmt,
    a pinned Rust toolchain, Windows (llvm-mingw), Linux cross and Android cross-compilation,
    and your own extra instructions. A derived image is tagged with a hash
    of its Dockerfile, so identical specs build once and are reused.
  - Build containers: started from a build image, running `sleep infinity`
    with /work as the working directory, cargo's registry and git caches on
    named volumes that outlive the container, and cargo's target folder on
    a volume of its own. Reusable by settings; kept or removed on dispose.
  - Commands inside a container (cargo, rustup, sh, ...), with the exit code
    and both output streams on failure.
  - Source copy-in: a project folder (or an archive, extracted on the host)
    is copied into the container's /work through the Docker Engine API -
    no shared folders - skipping .git, target, node_modules and your own
    patterns.
  - Cargo inside the container: build, check, test, doc, clippy, fmt and
    clean with --message-format=json (and fetch, which downloads the
    dependencies into the container's cargo cache volume without
    compiling), returning the SIBLING package's
    result model unchanged (CargoCommandResult, CargoTestResult,
    CompilerDiagnostic, BuildArtifact, CargoProgress), with cancellation
    that kills every compiler process in the container.
  - License gate inside the container: `cargo metadata` runs in the
    container and the sibling package's LicenseAuditor applies your
    LicensePolicy BEFORE anything compiles (or, with
    LicenseGateMode.ReportOnly, reports the audit and builds anyway).
  - Artifact retrieval: executables and libraries copied out to a host
    folder - flat, per target, or in the NuGet runtimes/<rid>/native
    layout - with size, SHA-256 and the execute bit.
  - One call, BuildAsync: engine check, image, container, copy, license
    gate, cargo per target (Linux x64/ARM64/RISC-V 64, Windows x64/ARM64,
    Android arm64/x64, macOS arm64/x64 and universal), retrieval and
    cleanup, with stage progress.
  - macOS, DEVELOPMENT AND TESTING ONLY: both Darwin targets linked with
    rustup's own rust-lld against a libSystem symbol stub the image derives
    from Rust's standard library (nothing from Apple is involved), on any
    engine architecture; universal (fat) files joined from the two slices;
    byte-for-byte the files a Mac produces with the sibling package's
    MacOSLinkProfile.
  - Cleanup: every container, derived image and volume the library creates
    carries labels, so cleanup removes exactly those - optionally only what
    one workbench instance created. The BuildKit build cache is the one
    thing labels cannot reach; CleanupOptions.PruneBuildCache (off by
    default, ENGINE-WIDE) is the opt-in for it.

It is the container-side sibling of CodeBrix.RustTools (which drives the
Rust toolchain installed on the machine itself) and reuses that package's
types: RustToolsException is the base of every exception here, and the
cargo options, results, diagnostics, progress, license policy and audit,
metadata and archive types are the sibling's own - code written against
one package's results works with the other's.


INSTALLATION
============
PackageId:  CodeBrix.RustTools.Docker.MitLicenseForever

    dotnet add package CodeBrix.RustTools.Docker.MitLicenseForever

There is no package named plain "CodeBrix.RustTools.Docker"; that is the
assembly and root namespace name.

Dependencies (pulled in automatically; the package pins the versions it was
built against):
  - CodeBrix.RustTools.MitLicenseForever     (the sibling package; MIT)
  - CodeBrix.Docker.MitLicenseForever        (talks to the Docker engine;
                                              MIT)
  - CodeBrix.TomlParse.BsdLicenseForever     (through CodeBrix.RustTools;
                                              BSD-2-Clause)

License:       MIT
Requirements:  .NET 10 or later, and at run time a RUNNING Docker engine
               in Linux-container mode - Docker Desktop on Windows and
               macOS, Docker Engine on Linux - reachable through a Unix
               socket, a Windows named pipe, tcp:// or ssh://. Android
               targets need an x86-64 engine; macOS targets work on any.
               Building a DERIVED image (any spec with layers) also needs
               the `docker` command-line tool with its buildx plugin on
               PATH, because image builds use BuildKit (the macOS layer is
               a Dockerfile heredoc). Using the plain `rust` image does not.
Network:       the first use pulls the official `rust` image (a large
               download: a full Debian build environment plus the Rust
               toolchain); layers download Debian packages, rustup
               components and, for Android, the NDK.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.RustTools.Docker;             // DockerRustWorkbench,
                                                 // options, timeouts,
                                                 // CleanupOptions/Result,
                                                 // the exceptions
    using CodeBrix.RustTools.Docker.Engine;      // DockerEngineInfo,
                                                 // DockerAvailability,
                                                 // DockerGuidance(+Provider)
    using CodeBrix.RustTools.Docker.Images;      // BuildImageSpec,
                                                 // DockerfileGenerator,
                                                 // RustBuildImage,
                                                 // CrossTarget(+Info)
    using CodeBrix.RustTools.Docker.Containers;  // BuildContainer,
                                                 // BuildContainerOptions,
                                                 // SourceCopyProgress/Result
    using CodeBrix.RustTools.Docker.Artifacts;   // ArtifactRetrievalOptions,
                                                 // ArtifactLayout,
                                                 // RetrievedArtifact, ...
    using CodeBrix.RustTools.Docker.Builds;      // ContainerBuildRequest,
                                                 // ContainerBuildResult,
                                                 // BuildStage,
                                                 // ContainerBuildProgress
    using CodeBrix.Docker;                       // only for
                                                 // DockerClientOptions,
                                                 // MountSpec, ResourceLimits

Types from the sibling package that appear in this API (add the usings
you need):
    CodeBrix.RustTools                 RustToolsException (base exception),
                                       LicensePolicyViolationException,
                                       SourceArchiveException
    CodeBrix.RustTools.Building        CargoBuildOptions, CargoCommand,
                                       CargoCommandResult, CargoTestResult,
                                       CompilerDiagnostic, BuildArtifact,
                                       CargoProgress (+Phase)
    CodeBrix.RustTools.Licensing       LicensePolicy, LicenseAuditReport
    CodeBrix.RustTools.Projects        CargoMetadata, CargoMetadataOptions
    CodeBrix.RustTools.Archives        ExtractedSource,
                                       SourceArchiveExtractor,
                                       SourceArchiveOptions
    CodeBrix.RustTools.Toolchain       OperatingSystemKind,
                                       HostOperatingSystem, ToolVersion


CORE API REFERENCE
==================

DockerRustWorkbench - THE ENTRY POINT
-------------------------------------
    public sealed class DockerRustWorkbench : IDisposable
    {
        public DockerRustWorkbench(DockerRustWorkbenchOptions options = null);

        public DockerRustWorkbenchOptions Options { get; }   // private copy
        public string InstanceId { get; }                    // 32 hex digits

        Task<DockerEngineInfo> CheckEngineAsync(CancellationToken ct = default);
        IReadOnlyList<DockerGuidance> GetDockerGuidance(DockerEngineInfo info);

        // --- foreign platforms (see FOREIGN PLATFORMS) -----------------
        const string PlatformProbeImage = "busybox:latest";
        Task<PlatformProbeResult> ProbePlatformAsync(string platform,
            CancellationToken ct = default);          // cached per platform
        Task<bool> CanRunPlatformAsync(string platform,
            CancellationToken ct = default);          // ProbePlatformAsync().CanRun

        Task<RustBuildImage> EnsureImageAsync(BuildImageSpec spec = null,
            IProgress<string> progress = null, CancellationToken ct = default);

        Task<BuildContainer> StartContainerAsync(RustBuildImage image,
            BuildContainerOptions options = null,
            CancellationToken ct = default);

        Task<string> ExecAsync(BuildContainer container,
            IReadOnlyList<string> command, CancellationToken ct = default);

        // --- one call -------------------------------------------------
        Task<ContainerBuildResult> BuildAsync(ContainerBuildRequest request,
            IProgress<ContainerBuildProgress> progress = null,
            CancellationToken ct = default);
        event EventHandler<ContainerBuildProgress> Progress;

        // --- step by step, on a container you started -----------------
        Task<SourceCopyResult> CopySourceAsync(BuildContainer container,
            string hostDirectory, IEnumerable<string> extraExcludes = null,
            IProgress<SourceCopyProgress> progress = null,
            CancellationToken ct = default);
        Task<SourceCopyResult> CopySourceIntoAsync(BuildContainer container,
            string hostDirectory, string containerSubdirectory,
            IEnumerable<string> extraExcludes = null,
            IProgress<SourceCopyProgress> progress = null,
            CancellationToken ct = default);
        Task<CargoCommandResult> RunCargoAsync(BuildContainer container,
            CargoCommand command, CargoBuildOptions options = null,
            CrossTarget target = CrossTarget.LinuxNative,
            IProgress<CargoProgress> progress = null,
            CancellationToken ct = default);
        Task<CargoTestResult> TestAsync(BuildContainer container,
            CargoBuildOptions options = null,
            IProgress<CargoProgress> progress = null,
            CancellationToken ct = default);
        Task<CargoMetadata> GetMetadataAsync(BuildContainer container,
            CargoMetadataOptions options = null,
            CancellationToken ct = default);
        Task<LicenseAuditReport> AuditLicensesAsync(BuildContainer container,
            LicensePolicy policy, CargoMetadataOptions metadataOptions = null,
            CancellationToken ct = default);
        Task<ArtifactRetrievalResult> RetrieveArtifactsAsync(
            BuildContainer container, IEnumerable<BuildArtifact> artifacts,
            ArtifactRetrievalOptions options,
            CrossTarget target = CrossTarget.LinuxNative,
            CancellationToken ct = default);
        Task<ArtifactRetrievalResult> RetrieveMacOSUniversalArtifactsAsync(
            BuildContainer container,
            IEnumerable<BuildArtifact> arm64Artifacts,
            IEnumerable<BuildArtifact> x64Artifacts,
            ArtifactRetrievalOptions options, CancellationToken ct = default);
        Task<string> ExportMacOSSysrootAsync(BuildContainer container,
            string hostDirectory, CancellationToken ct = default);

        Task<CleanupResult> CleanupAsync(CleanupOptions options = null,
            CancellationToken ct = default);

        void Dispose();
    }

  - The constructor does not contact the engine. It copies the options and
    throws ArgumentException when a required option (image, tag, label
    prefix, volume / repository / container-name prefix, timeouts) is blank.
  - One workbench owns one Docker client: create one and keep it. It is safe
    to use from several threads.
  - CheckEngineAsync NEVER throws for a Docker-side problem (only for your
    own cancellation): read IsAvailable and Issues on the result.
  - Every call that needs the engine checks it first (once per workbench,
    after the first success) and throws DockerUnavailableException -
    carrying the engine check and the guidance - when it cannot be used.
    BuildAsync always checks (its first stage).
  - EnsureImageAsync validates the spec BEFORE contacting the engine, so a
    bad apt package name or a macOS target fails fast.
  - ExecAsync runs the command in /work with the container's environment,
    WITHOUT a shell (use ["sh", "-c", "..."] for shell syntax), returns
    standard output, and throws ContainerBuildException (ExitCode,
    StandardOutput, StandardError, Command) when the exit code is not 0.
    Cancelling stops the wait, not the process in the container; removing
    the container stops everything in it.
  - The step-by-step methods refuse a removed container
    (ContainerBuildException). Each is described in its own section below:
    SOURCE COPY, CARGO IN THE CONTAINER, LICENSE GATE, ARTIFACTS, BUILDS;
    the two macOS-only ones under CROSS-COMPILATION TARGETS (macOS).
  - Progress is raised for every BuildAsync report and for the cargo
    progress of RunCargoAsync / TestAsync (as Building reports), on the
    thread that produced it; exceptions from handlers are ignored.
  - Dispose releases the Docker client only. Dispose BuildContainers first.

DockerRustWorkbenchOptions
--------------------------
    public sealed class DockerRustWorkbenchOptions   // all { get; set; }
    {
        DockerClientOptions ClientOptions;   // null
        string Endpoint;                     // null => DOCKER_HOST, then
                                             // the platform default
        string DefaultImage;                 // "rust"
        string DefaultTag;                   // "latest"
        string LabelPrefix;                  // "codebrix.rusttools.docker"
        string CacheVolumeName;              // "codebrix-rusttools-cargo-cache"
        string BuildImageRepository;         // follows LabelPrefix (see below);
                                             // "codebrix-rusttools-docker" for
                                             // the default prefix
        bool IsBuildImageRepositorySet { get; }   // set explicitly?
        string ContainerNamePrefix;          // "codebrix-rusttools"
        DockerRustTimeouts DefaultTimeouts;  // new()
        bool KeepContainerByDefault;         // false
        IList<string> DefaultExcludePatterns;
                 // [".git", "target", "node_modules", ".DS_Store"]

        static IReadOnlyList<string> StandardExcludePatterns { get; }
        static string RepositoryFromLabelPrefix(string labelPrefix);
        DockerRustWorkbenchOptions Clone();
        DockerClientOptions CreateClientOptions();
    }

  - ClientOptions is the full CodeBrix.Docker client configuration (endpoint,
    DockerCliPath, SSH executable and arguments, per-call timeout). Endpoint,
    when set, overrides ClientOptions.Endpoint. Supported endpoint forms:
    unix://, npipe://, tcp:// (and http://) and ssh://; https:// is NOT
    supported (reach a remote engine over ssh:// instead).
  - DefaultExcludePatterns lists the file and folder names that are never
    copied from a project folder into a build container.
  - Constants for every default are on the class (DefaultBaseImage,
    DefaultBaseTag, DefaultLabelPrefix, DefaultCacheVolumeName,
    DefaultBuildImageRepository, DefaultContainerNamePrefix).
  - BuildImageRepository follows LabelPrefix until you set it. The default
    prefix gives "codebrix-rusttools-docker" exactly; any other prefix gives
    RepositoryFromLabelPrefix(prefix): lower-cased, every run of characters
    other than a-z and 0-9 (dots included) turned into one '-', leading and
    trailing '-' removed - "codebrix.rusttools.scratch" gives
    "codebrix-rusttools-scratch". A value you set always wins (even the
    default name); setting null goes back to following the prefix; Clone
    keeps whichever it is. A prefix with no letters or digits cannot give a
    name: Validate (run when the workbench is created) then throws until
    you set BuildImageRepository yourself. The mapping is not one-to-one
    ("my.builds" and "my-builds" give the same name) - give such
    workbenches explicit repositories.
  - ISOLATING WORKBENCHES. Two workbenches share nothing on one engine only
    when they differ in all three names:
        LabelPrefix          - what CleanupAsync finds and removes; the
                               derived-image repository now follows it
        CacheVolumeName      - the cargo registry/git cache volumes
        ContainerNamePrefix  - container and target-volume names
    for example
        new DockerRustWorkbenchOptions
        {
            LabelPrefix         = "codebrix.rusttools.scratch",
            CacheVolumeName     = "codebrix-rusttools-scratch-cargo-cache",
            ContainerNamePrefix = "codebrix-rusttools-scratch",
        }   // BuildImageRepository => "codebrix-rusttools-scratch"
    With the same LabelPrefix the workbenches see (and clean up) each
    other's resources; OnlyThisWorkbench narrows a cleanup to one instance
    but not the reuse of images, caches or kept containers. If you set
    BuildImageRepository explicitly, give each isolated workbench its own,
    or two prefixes share (and one prefix's RemoveImages never sees) the
    same derived image.

DockerRustTimeouts
------------------
    TimeSpan ApiCall          = 100 s   each ordinary Engine API call (used as
                                        the client's DefaultTimeout when
                                        ClientOptions is null)
    TimeSpan ImagePreparation = 60 min  base-image pull + derived build
    TimeSpan Command          = 30 min  one ExecAsync command, and the
                                        default limit of one cargo run
                                        (RunCargoAsync, TestAsync, each
                                        BuildAsync target, cargo metadata)
    DockerRustTimeouts Clone();
Exceeding ImagePreparation, or Command in ExecAsync or cargo metadata,
surfaces as ContainerBuildException with an inner TimeoutException; a cargo
run that exceeds its limit (CargoBuildOptions.Timeout, else Command) is
killed and returns a result with TimedOut = true. ApiCall is enforced by the
Docker client itself. Timeout.InfiniteTimeSpan disables a limit.


ENGINE (CodeBrix.RustTools.Docker.Engine)
-----------------------------------------
    public static class DockerAvailability
    {
        static Task<DockerEngineInfo> CheckAsync(
            DockerClientOptions options = null, CancellationToken ct = default);
    }
The same check the workbench runs, usable without a workbench. Never throws
for a Docker-side problem.

    public sealed class DockerEngineInfo
    {
        bool   IsAvailable          // answered AND runs Linux containers
        string Endpoint             // as resolved (unix:///var/run/docker.sock)
        string ServerVersion, ApiVersion
        string OperatingSystem      // "Debian GNU/Linux 13", "Docker Desktop"
        string OsType               // "linux" | "windows"
        string Architecture         // "x86_64" | "aarch64"
        bool   IsLinuxDaemon, IsDockerDesktop, DockerCliFound
        OperatingSystemKind HostOperatingSystem    // this machine
        IReadOnlyList<DockerEngineIssue> Issues    // empty when all is well
        string ErrorMessage         // the Docker client's explanation, or null
        string NativeLinuxTriple    // x86_64-unknown-linux-gnu /
                                    // aarch64-unknown-linux-gnu /
                                    // riscv64gc-unknown-linux-gnu / null
        string NativeLinuxRuntimeIdentifier   // linux-x64 / linux-arm64 /
                                              // linux-riscv64 / null
        string NativePlatform       // linux/amd64 / linux/arm64 /
                                    // linux/riscv64 / null
        bool   IsNativePlatform(string platform)  // null counts as native
    }

    public enum DockerEngineIssue
    {
        EndpointNotSupported,  // malformed endpoint or https://
        NotReachable,          // not installed, not running, wrong endpoint
        PermissionDenied,      // Linux: user not in the docker group
        NotLinuxDaemon,        // Docker Desktop in Windows-containers mode
        DockerCliNotFound,     // `docker` not on PATH (derived builds need it)
    }
DockerCliNotFound alone does NOT make IsAvailable false: the plain `rust`
image works without the CLI; only derived-image builds need it.

    public sealed class DockerGuidance
    {
        DockerGuidanceTopic Topic; OperatingSystemKind OperatingSystem;
        string Title; string Explanation;
        IReadOnlyList<string> Commands;   // in order; "#" lines are comments
        string DocumentationUrl;          // https://docs.docker.com/...
        bool RequiresAdministrator;       // sudo / elevated prompt
        bool RequiresNewSession;          // sign out and back in afterwards
        string ToString();                // title, explanation, commands, link
    }

    public enum DockerGuidanceTopic
    {
        InstallDocker, StartDocker, GrantDockerAccess, EnableWsl2Backend,
        SwitchToLinuxContainers, CheckEndpoint, InstallDockerCli,
        EnableForeignPlatforms,   // qemu-user binfmt handlers (see FOREIGN
                                  // PLATFORMS); from ProbePlatformAsync only
    }

    public static class DockerGuidanceProvider
    {
        static IReadOnlyList<DockerGuidance> ForEngine(DockerEngineInfo info);
        static IReadOnlyList<DockerGuidance> ForIssues(
            IEnumerable<DockerEngineIssue> issues, OperatingSystemKind os);
        static DockerGuidance GetGuidance(DockerGuidanceTopic topic,
                                          OperatingSystemKind os);
    }
What the guidance says, per host:
  Windows       winget install --exact --id Docker.DockerDesktop; WSL 2
                (wsl --install / wsl --update, "Use the WSL 2 based
                engine"); start Docker Desktop; docker-users group; switch
                to Linux containers (DockerCli.exe -SwitchLinuxEngine).
  macOS         brew install --cask docker (or the download page);
                open -a Docker.
  Debian-based  Docker's apt repository (keyring + sources list), then
                apt-get install docker-ce docker-ce-cli containerd.io
                docker-buildx-plugin; sudo systemctl start docker /
                enable docker; sudo usermod -aG docker $USER and sign in
                again (the explanation notes that docker-group membership is
                root-equivalent, and points Ubuntu users at the Ubuntu
                instructions).
  Other Linux   a pointer to https://docs.docker.com/engine/install/,
                systemctl and usermod as above.
  Foreign platforms (EnableForeignPlatforms): Debian-based
                `sudo apt install qemu-user-static binfmt-support`, then
                `ls /proc/sys/fs/binfmt_misc/`; other Linux a pointer to
                https://docs.docker.com/build/building/multi-platform/;
                Windows / macOS: Docker Desktop's own emulation, checked
                with `docker run --rm --platform linux/arm64 busybox uname -m`.
For NotReachable the order is install, (Windows: WSL 2,) start, (Linux:
grant access); each explanation says to skip a step that is already done.


IMAGES (CodeBrix.RustTools.Docker.Images)
-----------------------------------------
    public sealed class BuildImageSpec
    {
        string BaseImage    { get; set; }   // null => workbench DefaultImage
        string Tag          { get; set; }   // null => workbench DefaultTag
        string RustVersion  { get; set; }   // "stable" | "1.<minor>.<patch>"
        IList<string> AptPackages          { get; set; }   // Debian packages
        bool InstallClippyRustfmt          { get; set; }
        IList<CrossTarget> CrossTargets    { get; set; }
        IList<string> ExtraDockerfileLines { get; set; }   // 1 instruction each
        string ImageName    { get; set; }   // null => BuildImageRepository
        bool RefreshBaseImage { get; set; } // pull even when present
        string Containerfile { get; set; }  // null; host path: build the
                                            // base from it ("bring your own")
        string ContainerfileContext { get; set; }  // null => its folder
        IDictionary<string, string> ContainerfileBuildArgs { get; set; }
        string Platform { get; set; }       // null => the engine's own;
                                            // "linux/arm64", ONE value

        string BaseImageReference { get; }  // "rust:latest" etc.
        bool   RequiresDerivedImage { get; }
        bool   UsesContainerfile { get; }
        BuildImageSpec Clone();
    }

    public static class DockerfileGenerator
    {
        static string Generate(BuildImageSpec spec);
        static string ComputeSpecHash(string dockerfileText);  // 16 hex digits
        static string ComputeSpecHash(string dockerfileText,
                                      string platform);  // the same without a
                                      // platform; "platform=..." appended with one
        static string DerivedImageReference(BuildImageSpec spec,
            string defaultRepository = "codebrix-rusttools-docker");
        const string AndroidNdkVersion, AndroidNdkUrl, AndroidNdkSha1,
                     AndroidNdkHome ("/opt/android-ndk"),
                     WorkDirectory ("/work"), HeaderLine;
    }

The generated Dockerfile, in this fixed order (each part only when asked
for):
    # Generated by CodeBrix.RustTools.Docker. ...      (header comment)
    FROM <BaseImage>:<Tag>
    RUN rustup default <RustVersion>
    RUN apt-get update && apt-get install -y --no-install-recommends
        <packages, sorted, de-duplicated> && rm -rf /var/lib/apt/lists/*
    RUN rustup component add clippy rustfmt
    <Windows layer (llvm-mingw)>
    <Android layer>
    <ExtraDockerfileLines>
    WORKDIR /work
    ENV CARGO_TERM_COLOR=never RUSTUP_AUTO_INSTALL=0

The text is deterministic (Unix line endings, final newline), so the derived
image tag - the first 16 hex digits of the SHA-256 of the text - is stable:
the same spec always maps to the same image, and a changed spec to a new
one. Generate throws ArgumentException for values that are not safe in a
Dockerfile (an apt name with spaces or shell characters, a Rust version
like "stable && ...", a multi-line extra instruction, an invalid image name or
tag) and CrossTargetNotSupportedException for macOS targets.

    public sealed class RustBuildImage
    {
        string ImageName, Tag, ImageId; string Reference { get; }
        BuildImageSpec Spec;          // with defaults filled in
        string Platform;              // BuildImageSpec.Platform, normalized;
                                      // null = the engine's own
        string Dockerfile, SpecHash, BaseImageReference;
        bool IsBaseImage;             // spec had no layers
        bool WasBasePulled;           // pulled during this call
        bool WasBuilt;                // derived (or Containerfile) image
                                      // built during this call
        bool WasReused { get; }       // already on the engine
        string ContainerfileImageReference;  // null without a Containerfile
        bool WasContainerfileImageBuilt;
    }

EnsureImageAsync, step by step:
  1. Pull the base image when it is missing (or when RefreshBaseImage).
     Pull output lines go to the IProgress<string>.
  2. No layers  -> the base image is the result (IsBaseImage = true).
  3. Layers     -> if <ImageName>:<hash> exists it is reused; otherwise the
     Dockerfile is written to a temporary build context and built with
     `docker build` (BuildKit; build output goes to the progress), labelled
     <prefix>=1, <prefix>.workbench, <prefix>.kind=image and
     <prefix>.spec-hash=<hash>.
A derived image is reused for as long as its Dockerfile text is unchanged -
even when the moving base tag (for example rust:latest) has moved on. Set
RefreshBaseImage and change the spec (for example pin RustVersion) to
rebuild on a newer base.

WHAT THE GENERATED LAYERS NEED: they are Debian-based (apt-get for
AptPackages and the Linux cross / Windows / Android / macOS layers; rustup
for RustVersion and InstallClippyRustfmt), so they fit the official rust
image and other Debian bases only, and building them needs the docker CLI
(CodeBrix.Docker builds images with `docker build`, BuildKit/buildx). A
base that is not Debian - manylinux / AlmaLinux with dnf, a pinned system
toolchain, cross sysroots - cannot be layered with apt. Bring your own
Containerfile instead (below); that is the supported route.

BRING YOUR OWN CONTAINERFILE (BuildImageSpec.Containerfile):
    var image = await wb.EnsureImageAsync(new BuildImageSpec
    {
        Containerfile = "linux/Containerfile.x86_64",   // host path
        ContainerfileContext = null,                    // => linux/
        ContainerfileBuildArgs = { ["RUST_TOOLCHAIN"] = "1.93" },
    });
  1. The workbench hashes the Containerfile's bytes, the build arguments
     (sorted) and every file of the context folder (relative path + SHA-256;
     symbolic links by target, never followed) and looks for
     <ImageName>:containerfile-<hash>. Missing (or RefreshBaseImage, which
     also pulls the FROM image afresh) -> it is built through the engine's
     CLI with -f <Containerfile>, the build arguments and the workbench's
     labels (<prefix>.kind=image, <prefix>.spec-hash=<hash>), so
     CleanupAsync(RemoveImages) removes it. A second EnsureImageAsync with
     the same inputs reuses it (WasContainerfileImageBuilt false). Files a
     .dockerignore excludes still count: editing one costs a rebuild, never
     a stale image. Keep the context folder small.
  2. No layers in the spec -> that image IS the build image (IsBaseImage =
     true). It must then provide Rust itself: rustup with
     CARGO_HOME=/usr/local/cargo (the official rust image's layout - the
     cargo cache volumes mount at /usr/local/cargo/registry and /git),
     cargo on PATH, and sh. This is the usual form for a non-Debian base.
  3. Layers in the spec -> they are generated FROM that image (the derived
     image's BaseImageReference is <ImageName>:containerfile-<hash>); the
     layer rules above apply (rustup for RustVersion/clippy, Debian for apt
     and the cross layers).
  BaseImage and Tag must stay unset with a Containerfile (ArgumentException);
  a missing Containerfile or context folder throws FileNotFoundException /
  DirectoryNotFoundException before the engine is touched.
  Podman without the docker CLI: point CodeBrix.Docker at podman for builds,
      ClientOptions = new DockerClientOptions { Endpoint =
          "unix:///run/user/1000/podman/podman.sock", DockerCliPath = "podman" }
  `podman build` takes the same arguments and writes to the same local
  storage the Podman API socket serves.


CROSS-COMPILATION TARGETS
-------------------------
    public enum CrossTarget
    {
        LinuxNative, LinuxX64, LinuxArm64, LinuxRiscV64,
        WindowsX64Gnullvm, WindowsArm64Gnullvm, AndroidArm64, AndroidX64,
        MacOSArm64, MacOSX64, MacOSUniversal,
    }

    public sealed class CrossTargetInfo
    {
        static IReadOnlyList<CrossTargetInfo> All { get; }
        static CrossTargetInfo Get(CrossTarget target);
        static bool TryFromTriple(string triple, out CrossTargetInfo info);

        CrossTarget Target; string Triple; string RuntimeIdentifier;
        string AndroidAbi;            // "arm64-v8a" | "x86_64" | null
        string ExecutableExtension;   // ".exe" | ""
        string SharedLibraryPrefix, SharedLibraryExtension;
        string StaticLibraryPrefix, StaticLibraryExtension;
        bool IsSupported, RequiresLayer, IsAndroid, IsMacOS,
             IsMacOSUniversal, IsLinux, IsLinuxNative, IsWindows;
        string GnuToolPrefix;                 // aarch64-linux-gnu; Linux only
        IReadOnlyList<string> DebianCrossPackages;   // Linux only
        bool IsNativeOn(DockerEngineInfo engine)     // the engine's own arch
        static CrossTarget? NativeLinuxTargetFor(DockerEngineInfo engine)
        static CrossTarget Resolve(CrossTarget target, DockerEngineInfo engine)
                                              // LinuxNative -> the engine's
        string Notes;
        string ExecutableFileName(string name);
        string SharedLibraryFileName(string crateName);   // hyphens -> _
        string StaticLibraryFileName(string crateName);
        const string MacOSDevelopmentOnlyNotice, MacOSBuildNotes,
                     MacOSUniversalNotes, WindowsGnullvmNotes;
        static IReadOnlyList<CrossTarget> MacOSUniversalSlices
                                          // [MacOSArm64, MacOSX64]
    }

    Target         Triple                     RID            Shared lib
    LinuxNative    (the engine's own: one of the three below)
    LinuxX64       x86_64-unknown-linux-gnu   linux-x64      libX.so
    LinuxArm64     aarch64-unknown-linux-gnu  linux-arm64    libX.so
    LinuxRiscV64   riscv64gc-unknown-linux-gnu linux-riscv64 libX.so
    WindowsX64Gnullvm   x86_64-pc-windows-gnullvm  win-x64       X.dll (+.exe)
    WindowsArm64Gnullvm aarch64-pc-windows-gnullvm win-arm64     X.dll (+.exe)
    AndroidArm64   aarch64-linux-android      android-arm64  libX.so
    AndroidX64     x86_64-linux-android       android-x64    libX.so
    MacOSArm64     aarch64-apple-darwin       osx-arm64      libX.dylib
    MacOSX64       x86_64-apple-darwin        osx-x64        libX.dylib
    MacOSUniversal universal-apple-darwin     osx            libX.dylib
                   (a fat file: both slices)                 (+ programs)

LinuxNative: the build container's own architecture - the default target.
  It is resolved before anything is built (CrossTargetInfo.Resolve) to
  LinuxX64 on an x86-64 engine, LinuxArm64 on an ARM64 engine (Docker
  Desktop on Apple Silicon) or LinuxRiscV64 on a RISC-V engine, and
  ContainerBuildResult.Results and the retrieved files carry the RESOLVED
  target: on Apple Silicon, result.Results[CrossTarget.LinuxArm64] and
  runtimes/linux-arm64/native. No layer; cargo runs without --target.
  DockerEngineInfo.NativeLinuxTriple and CrossTargetInfo
  .NativeLinuxTargetFor(engine) tell you which it is.

LinuxX64 / LinuxArm64 / LinuxRiscV64: a concrete Linux architecture. The one
  that is the engine's own builds natively, exactly like LinuxNative. Each
  other one is CROSS-COMPILED with Debian's cross toolchain for it: the
  image layer installs gcc-<arch>-linux-gnu, g++-<arch>-linux-gnu and
  libc6-dev-<arch>-cross (the target's glibc headers and libraries) and
  adds the rustup target; cargo then runs with --target and the cross gcc
  as linker, CC/CXX and ar (CodeBrix.RustTools.LinuxLinkProfile, the same
  variables cargo-ndk-style builds use). Because the cross glibc headers
  are there, crates that compile C work too. An ARM64 engine builds x86-64
  and RISC-V binaries, an x86-64 engine builds ARM64 and RISC-V ones; no
  emulation. The container cannot run a foreign-architecture binary, so
  test them on a machine of that architecture (TestAsync always tests the
  engine's own). Output is for glibc-based distributions.

WindowsX64Gnullvm / WindowsArm64Gnullvm: Rust's windows-gnullvm targets,
  built with llvm-mingw - clang, lld and the mingw-w64 runtime, all open
  source (LLVM under Apache-2.0 with LLVM exceptions, mingw-w64 under its
  own permissive licences; the toolchain's LICENSE.TXT and COPYING.* files
  stay in /opt/llvm-mingw). The Windows layer downloads the pinned
  llvm-mingw release (DockerfileGenerator.LlvmMingwVersion) for the
  engine's own architecture from github.com/mstorsjo/llvm-mingw, verifies
  it against the pinned SHA-256 (LlvmMingwSha256X64 / LlvmMingwSha256Arm64),
  unpacks it to /opt/llvm-mingw (LLVM_MINGW_HOME) and adds both rustup
  targets; one layer serves x64 and ARM64, on x86-64 and ARM64 engines
  alike. cargo runs with --target and the target's clang wrapper as linker
  and C/C++ compiler, llvm-ar as archiver and
  -C target-feature=+crt-static (CodeBrix.RustTools.WindowsGnullvmLinkProfile;
  also BINDGEN_EXTRA_CLANG_ARGS_<triple> = "--target=<machine>-w64-mingw32
  --sysroot=/opt/llvm-mingw" so bindgen finds the mingw-w64 headers - add
  libclang-dev to AptPackages for bindgen crates),
  which links llvm-mingw's unwinder statically: the output imports only
  Windows' own DLLs - the Universal C Runtime and the system libraries -
  and so needs Windows 10 or later. NO Microsoft SDK, compiler or runtime
  is downloaded, and nothing is redistributed. Programs and DLLs use the C
  ABI (P/Invoke works); the C++ ABI is Itanium, not MSVC's, so an
  MSVC-built .lib cannot be linked in. The container cannot run the
  output; test it on Windows.

AndroidArm64 / AndroidX64: the layer downloads the Android NDK (the LTS
  release named by DockerfileGenerator.AndroidNdkVersion) from
  dl.google.com, verifies its published SHA-1, unpacks it to
  /opt/android-ndk (ANDROID_NDK_HOME) and adds the aarch64-linux-android
  and x86_64-linux-android rustup targets. Asking for either Android target
  adds the same single layer.
  X86-64 ENGINE REQUIRED: the NDK ships its compilers and linkers for x86-64
  Linux hosts only, so Android cross-compilation needs an x86-64 (amd64)
  Linux Docker engine. EnsureImageAsync (for a spec with an Android target)
  and BuildAsync (right after the engine check, before any image work)
  throw CrossTargetNotSupportedException - naming the engine's
  architecture - when DockerEngineInfo.Architecture is anything else. On
  Docker Desktop for Apple Silicon that would mean an amd64 build image
  running under emulation, which this library does not set up by itself:
  only an explicit BuildImageSpec.Platform = "linux/amd64" (FOREIGN
  PLATFORMS) makes the image and the container amd64, and then the check
  is made against that platform. That combination is not exercised by this
  package's tests.
  HOW ANDROID BUILDS RUN: RunCargoAsync / BuildAsync run
  `cargo build --target <triple>` directly, with the variables that point
  cargo and C build scripts at the NDK (CC_, CXX_, AR_, RANLIB_<triple> and
  CARGO_TARGET_<TRIPLE>_LINKER / _AR pointing at the NDK's
  <triple><level>-clang and llvm-ar, and BINDGEN_EXTRA_CLANG_ARGS_<triple> =
  "--target=<triple><level> --sysroot=<NDK sysroot>" for crates whose build
  script runs bindgen - recent NDKs refuse the bare triple bindgen passes;
  bindgen also needs libclang in the image: AptPackages = { "libclang-dev" })
  - Android API level 24 (Android 7.0) by default; another level with
  ContainerBuildRequest.AndroidApiLevel (one-call builds) or
  BuildContainerOptions.AndroidApiLevel / BuildContainer.AndroidApiLevel
  (step by step; for example 33 for a library that links a native package
  built for Android 13). They
  come from CodeBrix.RustTools.AndroidLinkProfile, the same type the sibling
  package applies to a host's own NDK, so a container and a host set
  identical variables. The cargo-ndk tool is neither installed nor used: it
  keeps cargo's JSON messages to itself, so diagnostics, artifacts and
  progress would be lost. If you want it for your own ExecAsync use, add it to the image:
      ExtraDockerfileLines = { "RUN cargo install --locked cargo-ndk" }
  LICENSING: the Android NDK is licensed under the Android Software
  Development Kit License Agreement
  (https://developer.android.com/studio/terms). It is downloaded at YOUR
  request when you build an image with an Android target, under that
  agreement; this package does not redistribute it, and it is never part
  of an image unless an Android target is requested. The generated
  Dockerfile states this in a comment.

MacOSArm64 / MacOSX64 / MacOSUniversal: DEVELOPMENT AND TESTING ONLY.
  READ THIS:

  ************************************************************************
  *  IMPORTANT NOTICE: macOS binaries produced by                        *
  *  CodeBrix.RustTools.Docker are built in Docker containers without    *
  *  Apple's SDK, tools or files and are for development and testing     *
  *  only. They must not be distributed or used in any production        *
  *  environment. Any macOS build that will be distributed or run in     *
  *  production must be produced on a Mac and can be created using       *
  *  CodeBrix.RustTools, where Apple's toolchain, SDK and code signing   *
  *  are available under Apple's own license terms.                      *
  ************************************************************************

  How the layer works: it adds rustup's precompiled standard libraries for
  both Darwin targets and the llvm-tools component, then derives - from
  those libraries and a probe program it compiles - the names of the
  symbols Rust's std and compiled Rust code import from
  /usr/lib/libSystem.B.dylib, adds the ISO C math function names and the
  Darwin library calls LLVM emits for float and memory operations (pow,
  log, sin, fmod, __sincos_stret, __exp10, memset_pattern16, ...; names
  only, so crates that do float math link), and writes them as a TAPI text
  stub: /opt/codebrix/macos-sysroot/usr/lib/libSystem.tbd, with libc, libm,
  libpthread and libdl aliases (the names rustc links with), plus the
  symbols-<triple>.txt lists and a README.txt. cargo then links with
  rustup's own rust-lld in its ld64 flavour (/opt/codebrix/bin/rust-lld)
  against that stub: SDKROOT names the sysroot (rustc passes -syslibroot
  itself and never looks for Xcode), MACOSX_DEPLOYMENT_TARGET is 11.0, -S
  keeps the debug map (object paths and timestamps) out of the output, and
  the cargo registry folder is remapped to /cargo-registry-src. Nothing
  from Apple is downloaded, copied or needed, and the engine's architecture
  does not matter: an ARM64 engine (Docker Desktop on Apple Silicon)
  builds x86_64 slices and an x86-64 engine builds arm64 slices. The
  linker writes the ad-hoc signature Apple Silicon requires to launch a
  binary. The same settings are CodeBrix.RustTools.MacOSLinkProfile, which
  ContainerCargoRunner applies for you.

  MacOSUniversal is a RETRIEVAL target, not a cargo target: BuildAsync
  builds MacOSArm64 and MacOSX64 and joins every program and dynamic
  library that both produced into one universal (fat) file with
  CodeBrix.RustTools.MacOSUniversalBinary (the same layout Apple's lipo
  makes; no lipo involved). ContainerBuildResult.Results holds the two
  slice builds (keys MacOSArm64 and MacOSX64); the universal files carry
  RetrievedArtifact.Target = MacOSUniversal and the arm64 slice as their
  ContainerPath. Name the slices in Targets as well when you want them
  retrieved too. RunCargoAsync rejects MacOSUniversal with
  ArgumentException; step by step, run cargo for both slices and call:

    Task<ArtifactRetrievalResult> RetrieveMacOSUniversalArtifactsAsync(
        BuildContainer container,
        IEnumerable<BuildArtifact> arm64Artifacts,
        IEnumerable<BuildArtifact> x64Artifacts,
        ArtifactRetrievalOptions options, CancellationToken ct = default)

  Install name: a cdylib's recorded install name (LC_ID_DYLIB) defaults to
  its build path, so the same source built in two places gives two
  different files. Set CargoBuildOptions.MacOSInstallName (for example
  "@rpath/libmy_lib.dylib"); BuildAsync defaults it to
  MacOSLinkProfile.DefaultInstallName(project) - the conventional name -
  when the package declares exactly one cdylib. It is passed as
  -install_name and ignored for executables.

  Identical bytes on a Mac (the container build as a dev/test twin of the
  Mac build): copy the sysroot out with

    Task<string> ExportMacOSSysrootAsync(BuildContainer container,
        string hostDirectory, CancellationToken ct = default)

  (the container's image must have the macOS layer; the folder then holds
  usr/lib/libSystem.tbd and the aliases), then on the Mac apply
  CodeBrix.RustTools.MacOSLinkProfile.ForToolchain(toolchain, thatFolder)
  to the build options (ApplyTo(options, triple, installName)) and build
  with CodeBrix.RustTools. With the same Rust release on both sides the
  program and the library come out byte-for-byte identical, ad-hoc
  signature included - the sibling package's AGENT-README (MacOSLinkProfile)
  has the details and the levers.

  Limits: pure-Rust dependency trees only (a crate that compiles C - the cc
  crate, rusqlite "bundled", aws-lc-rs - needs Apple's headers and so needs
  a Mac); nothing that links an Apple framework beyond libSystem; ad-hoc
  signatures only, no signing identity, no notarisation. The per-target
  CARGO_TARGET_<TRIPLE>_RUSTFLAGS in CargoBuildOptions.Environment EXTENDS
  the layer's own flags (they come first); a plain RUSTFLAGS variable would
  override them, so do not set that one for macOS targets. The
  first image build downloads the two Darwin targets and llvm-tools from
  static.rust-lang.org and takes well under a minute.


CONTAINERS (CodeBrix.RustTools.Docker.Containers)
-------------------------------------------------
    public sealed class BuildContainerOptions
    {
        string Name { get; set; }             // null => generated
        bool ReuseExisting { get; set; }      // false
        bool? KeepOnDispose { get; set; }     // null => KeepContainerByDefault
        IDictionary<string, string> Env { get; set; }
        ResourceLimits ResourceLimits { get; set; }   // CodeBrix.Docker
        IList<MountSpec> Mounts { get; set; }         // CodeBrix.Docker
        string TargetVolumeName { get; set; } // null => <name>-target
        bool NetworkDisabled { get; set; }    // false; true => network mode "none"
        int AndroidApiLevel { get; set; }     // 24; the container's starting
                                              // BuildContainer.AndroidApiLevel
                                              // (not part of the reuse key)
        string Platform { get; set; }         // null => the image's platform
                                              // (RustBuildImage.Platform), else
                                              // the engine's own
        BuildContainerOptions Clone();
    }

    public sealed class BuildContainer : IAsyncDisposable
    {
        const string WorkDirectory     = "/work";
        const string CargoRegistryPath = "/usr/local/cargo/registry";
        const string CargoGitPath      = "/usr/local/cargo/git";
        const string TargetPath        = "/work/target";

        string Id, Name; RustBuildImage Image;
        string Platform;                     // the container's platform; null =
                                             // the engine's own
        string CacheVolumeName; string CacheGitVolumeName;  // + "-git"
        string TargetVolumeName; string ReuseKey;
        bool WasReused; bool KeepOnDispose { get; set; }
        int AndroidApiLevel { get; set; }    // Android targets are linked for
                                             // it; 21 or later
        bool IsRunning; bool IsRemoved;

        Task<bool> RefreshStateAsync(CancellationToken ct = default);
        Task StopAndRemoveAsync(bool removeTargetVolume = true,
                                CancellationToken ct = default);
        ValueTask DisposeAsync();
    }

What StartContainerAsync creates:
  - Volumes (created if missing, labelled): <CacheVolumeName> mounted at
    /usr/local/cargo/registry, <CacheVolumeName>-git at /usr/local/cargo/git,
    and the target volume at /work/target. The cache volumes are SHARED by
    every container of every workbench with the same CacheVolumeName and
    persist until CleanupAsync removes them - that is what makes the second
    build fast.
  - The container: image = RustBuildImage.Reference, command
    ["sleep", "infinity"], working directory /work, environment
    CARGO_TERM_COLOR=never and RUSTUP_AUTO_INSTALL=0 (plus Env, which may
    override them), labels <prefix>=1, <prefix>.workbench=<InstanceId>,
    <prefix>.kind=container, <prefix>.reuse-key, <prefix>.spec-hash, then
    the extra Mounts and the ResourceLimits. With NetworkDisabled the
    container's network mode is "none": it has only a loopback interface and
    nothing inside it (cargo, a build script, a proc macro) can resolve or
    reach a registry or any other host.
  - Name: options.Name, else <ContainerNamePrefix>-<12 random hex>, or with
    ReuseExisting <ContainerNamePrefix>-<reuse key>.
ReuseExisting: the reuse key is a hash of the image (reference and id), the
cache volume, Name, TargetVolumeName, NetworkDisabled, the platform (when
one is set; an arm64 container is never reused for an x86-64 request), the
merged environment, the extra mounts and the limits (a network-less container is
never reused for a networked request, nor the other way round). A labelled container with the same key is returned
(WasReused = true); a stopped one is started again first. Otherwise a new
one is created.
Disposal: unless KeepOnDispose, DisposeAsync force-removes the container
(killing everything in it) and its target volume; Docker errors during
disposal are swallowed. StopAndRemoveAsync does the same but throws, and
leaves a target volume that another container still uses. Calling either
twice is harmless.

Fetch, then build offline (no network during the compile):
  The cargo cache volumes are shared by every container of the workbench,
  so download in a networked container and compile in a network-less one:

    await using (var online = await wb.StartContainerAsync(image))
    {
        await wb.CopySourceAsync(online, dir);
        var fetch = await wb.RunCargoAsync(online, CargoCommand.Fetch,
            new CargoBuildOptions { Locked = true });   // downloads everything
    }

    await using var offline = await wb.StartContainerAsync(image,
        new BuildContainerOptions { NetworkDisabled = true });
    await wb.CopySourceAsync(offline, dir);
    var build = await wb.RunCargoAsync(offline, CargoCommand.Build,
        new CargoBuildOptions { Locked = true, Offline = true });

  CargoBuildOptions.Offline alone only stops cargo itself from fetching;
  NetworkDisabled also stops build scripts and proc macros. A crate that is
  not in the cache fails the offline build with cargo's "attempting to make
  an HTTP request, but --offline was specified" error (Success false), never
  a hang.


FOREIGN PLATFORMS (BuildImageSpec.Platform, BuildContainerOptions.Platform)
---------------------------------------------------------------------------
An image and its build containers can be of another architecture than the
engine's own: a linux/arm64 or linux/riscv64 image on an x86-64 engine (or
linux/amd64 on an ARM64 engine). Such an image is built and run under
EMULATION, and inside it everything - rustup, cargo, rustc, build scripts -
runs as that architecture, so its Linux target is the container's NATIVE
target (no cross layer, no --target). This is the route for a toolchain
that only exists as an image of the target architecture, such as the
aarch64 and riscv64 manylinux images.

PREREQUISITE: the engine's host must be able to execute the architecture.
On a Linux engine host that is a qemu-user binfmt handler per architecture,
visible as /proc/sys/fs/binfmt_misc/qemu-aarch64 (arm64) or
/proc/sys/fs/binfmt_misc/qemu-riscv64 (riscv64); on Debian
`sudo apt install qemu-user-static binfmt-support` registers them. Docker
Desktop brings its own emulation. Without it every program of the image
fails with "exec format error". This package never installs anything: it
probes and returns guidance.

SLOWNESS: emulated builds are many times slower than native ones (rustup
alone takes minutes under qemu-user). Prefer the Debian cross layers
(CrossTargets) when the glibc of the official rust image is acceptable.

    public static class DockerPlatforms            // CodeBrix.RustTools.Docker.Engine
    {
        const string LinuxAmd64 = "linux/amd64", LinuxArm64 = "linux/arm64",
                     LinuxRiscV64 = "linux/riscv64";
        static string Normalize(string platform);   // trims, lower-cases;
                    // blank => null; a comma list or a malformed value throws
                    // ArgumentException (ONE platform: a build loads one
                    // single-platform image)
        static string MachineOf(string platform);   // linux/arm64 => aarch64
        static string ForArchitecture(string arch); // x86_64 => linux/amd64
        static string ForLinuxTarget(CrossTarget t);// LinuxArm64 => linux/arm64
        static bool   AreSame(string a, string b);  // variant ignored
    }

    var arm64 = await wb.EnsureImageAsync(new BuildImageSpec
    {
        Containerfile = "Containerfile.aarch64",     // FROM an arm64 image
        Platform = DockerPlatforms.LinuxArm64,
    });
    await using var c = await wb.StartContainerAsync(arm64);   // linux/arm64
    await wb.ExecAsync(c, ["uname", "-m"]);                    // "aarch64\n"
    var build = await wb.RunCargoAsync(c, CargoCommand.Build,
        new CargoBuildOptions { Release = true });   // LinuxNative = LinuxArm64
                                                     // here: no --target

What Platform does:
  - BuildImageSpec.Platform goes to the base image pull (CodeBrix.Docker's
    PullAsync with ImagePullOptions.Platform) and to `docker build
    --platform` for BOTH routes (the generated Dockerfile and the
    Containerfile). It is part of the image identity: the derived image's
    tag (ComputeSpecHash(text, platform)) and the Containerfile image's hash
    include it, so an arm64 image never reuses the x86-64 image of the same
    spec. A spec WITHOUT a platform keeps exactly the hash, the requests and
    the behaviour it had before the property existed.
  - RustBuildImage.Platform records it. BuildContainerOptions.Platform =
    null (the default) makes the container use the IMAGE's platform; a
    different value is the caller's explicit choice (the variant must be
    present locally - container creation never pulls). The platform is part
    of the container reuse key. BuildContainer.Platform reports it.
  - The container's native Linux target follows its platform: in a
    linux/arm64 container LinuxNative resolves to LinuxArm64, LinuxArm64
    builds without --target, a LinuxX64 build is a cross build (it needs the
    LinuxX64 cross layer in the image, which then is installed for arm64),
    the image layers are computed against the platform, and retrieval names
    the folders accordingly (runtimes/linux-arm64/native).
  - ContainerBuildRequest carries both (ImageSpec.Platform for the image,
    ContainerOptions.Platform for the container, the latter winning), so
    BuildAsync builds, runs and retrieves for the platform in one call; no
    other member is needed.
  - A multi-platform base used directly (no layers): on the containerd image
    store the inspect of the local tag shows the engine's own variant, so
    EnsureImageAsync pulls the requested variant (a quick manifest check
    when it is already local; offline it carries on with what is there).
    On the older graph-driver store a pull for another platform RE-POINTS
    the tag (CodeBrix.Docker's note), so the tag then names the foreign
    image for every user of it - keep foreign-platform work on a
    containerd-store engine, or use a Containerfile / derived image.

    public sealed class PlatformProbeResult
    {
        string Platform;                 // normalized
        PlatformProbeOutcome Outcome;
        bool   CanRun { get; }           // Native or Runs
        bool   IsEmulated;               // not the engine's own platform
        string ReportedMachine;          // what `uname -m` printed, e.g. aarch64
        string ProbeImage;               // busybox:latest, or null
        string Message;                  // includes the engine's words
        DockerGuidance Guidance;         // EnableForeignPlatforms for
                                         // NoEmulator, else null
    }

    public enum PlatformProbeOutcome
    {
        Native,      // the engine's own platform (or null): nothing was run
        Runs,        // a container of the platform ran and printed its machine
        NoEmulator,  // "exec format error": no qemu-user binfmt handler
        Refused,     // pull / create / start refused (no such variant, ...)
        Failed,      // ran but failed, or printed another machine
    }

ProbePlatformAsync(platform): pulls busybox:latest for the platform, runs
`uname -m` in a throw-away container created with that platform (labelled
like every container of the workbench; removed right after, also on
failure), and classifies the answer. It is cached per platform for the
lifetime of the workbench, never throws for a Docker-side problem with the
platform (only for an unusable engine - DockerUnavailableException - an
invalid platform - ArgumentException - or cancellation), and leaves that
platform's variant of busybox:latest on the engine (a base image: never
removed by CleanupAsync).


SOURCE COPY (CopySourceAsync)
-----------------------------
    Task<SourceCopyResult> CopySourceAsync(BuildContainer container,
        string hostDirectory, IEnumerable<string> extraExcludes = null,
        IProgress<SourceCopyProgress> progress = null,
        CancellationToken ct = default);

    public sealed class SourceCopyResult
    {
        string HostDirectory, ContainerDirectory;     // ..., "/work"
        long FileCount, TotalBytes;
        IReadOnlyList<string> ExcludePatterns;        // the ones applied
        TimeSpan Duration;
    }
    public sealed class SourceCopyProgress           // one per file sent
    {
        long FilesCopied, BytesCopied; string CurrentPath;
    }

  - Copies the CONTENTS of hostDirectory into /work over the Docker Engine
    API (a tar stream; nothing is buffered in memory, no folder sharing, so
    it works with remote ssh:// and tcp:// engines too).
  - /work is emptied first - except the target volume at /work/target and
    any other mount point - so files deleted on the host do not linger in
    a reused container.
  - Excluded: DockerRustWorkbenchOptions.DefaultExcludePatterns (.git,
    target, node_modules, .DS_Store) plus extraExcludes. A pattern WITHOUT
    a slash matches a name at ANY depth ("target", "*.log"); WITH a slash it
    is relative to hostDirectory ("docs/build"); a trailing slash means
    folders only; "**" crosses folders. Case sensitive.
  - Unix permission bits travel with the files from a Linux or macOS host.
  - CopySourceIntoAsync(container, hostDirectory, containerSubdirectory, ...)
    copies into /work/<containerSubdirectory> instead and empties ONLY that
    folder first, so two inputs (a tool and a project, say) can sit side by
    side in one container. The sub-folder is relative ("tools/x", "./x",
    ".hidden/x") or absolute below /work ("/work/x"); /work itself, the
    target volume (/work/target and below) and ".." are rejected with
    ArgumentException. Run cargo there with options.WorkingDirectory = the
    same sub-folder.

CARGO IN THE CONTAINER (RunCargoAsync, TestAsync, GetMetadataAsync)
-------------------------------------------------------------------
    var result = await wb.RunCargoAsync(container, CargoCommand.Build,
        options, CrossTarget.LinuxX64, progress, ct);

  - The result is the sibling package's CargoCommandResult (a
    CargoTestResult for CargoCommand.Test), built by the SAME argument
    builder and output parser the host-side CargoRunner uses: typed
    Diagnostics / Warnings / Errors / BuildScriptWarnings, Artifacts,
    RunnableArtifacts, StandardError, Duration, CommandLine
    ("cargo build --message-format=json ...").
  - PATHS IN THE RESULT ARE CONTAINER PATHS: artifacts under /work/target
    (/work/target/debug/app, /work/target/x86_64-pc-windows-gnullvm/debug/
    app.exe), WorkingDirectory "/work", diagnostic spans relative to the
    package. Use RetrieveArtifactsAsync to get files onto the host; do not
    pass these artifacts to the sibling's RunAsync.
  - The command runs as `setsid -w cargo <args>` in /work with
    CARGO_TARGET_DIR=/work/target (the target volume),
    CARGO_TERM_COLOR=never, RUSTUP_AUTO_INSTALL=0, then
    options.Environment (entries whose value is null are ignored - a
    variable cannot be removed inside the container). An entry replaces the
    library's own variable of the same name, EXCEPT the target's
    CARGO_TARGET_<TRIPLE>_RUSTFLAGS: its flags are appended to the library's
    flags for that target (Windows +crt-static; the macOS layer's linker
    flags), so extra flags never drop the ones the target needs.
  - options.WorkingDirectory is a CONTAINER folder here: null = /work, a
    relative path is below /work ("crates/cli" -> /work/crates/cli, for a
    workspace member; only "." segments such as a leading "./" are dropped,
    so ".hidden/x" -> /work/.hidden/x), an absolute path is used as is;
    ".." is rejected.
  - target: LinuxNative (the default) and the Linux target that is the
    engine's own keep options.TargetTriple (normally unset = the
    container's own target); every other target sets --target to its
    triple and, for a Linux target of another architecture, points cargo at
    the cross toolchain (see CROSS-COMPILATION TARGETS). The image must
    carry the target's layer, or cargo fails with a missing-target or
    missing-linker error in the result.
  - options.Timeout (default DockerRustTimeouts.Command): on expiry the run
    is killed and the result has TimedOut = true, ExitCode -1.
  - CANCELLATION kills cargo AND every process it started (rustc, build
    scripts, linkers) inside the container: each run carries a marker
    environment variable (CODEBRIX_RUSTTOOLS_RUN=<guid>) and every process
    carrying it is killed. Then OperationCanceledException is thrown, or,
    with options.ThrowOnCancellation = false, a result with WasCancelled.
    Killed processes may stay listed by `ps` as zombies (state Z) until the
    container is removed; they use no CPU or memory.
  - A failed compilation is a result with Success = false, never an
    exception.
  - TestAsync = RunCargoAsync(CargoCommand.Test) for the container's own
    target (TestsPassed, TestsFailed, Tests, FailedTestNames, ...).
  - GetMetadataAsync runs `cargo metadata --format-version 1` for
    /work/Cargo.toml and returns the sibling's CargoMetadata; its manifest
    paths, WorkspaceRoot ("/work") and TargetDirectory are container
    paths. A failure throws ContainerBuildException (exit code, stderr).

LICENSE GATE IN THE CONTAINER
-----------------------------
    var report = await wb.AuditLicensesAsync(container,
        LicensePolicy.Permissive());           // report, pass or fail

  - CargoBuildOptions.LicensePolicy works exactly as in the sibling
    package: RunCargoAsync (compiling commands: build, check, test, doc,
    clippy) and BuildAsync run `cargo metadata --filter-platform <triple>`
    INSIDE the container (with the options' Offline / Locked / Frozen /
    feature flags; the triple of the target being built), audit every
    crate of that graph with LicenseAuditor, and only then compile.
    BuildAsync runs one such `cargo metadata` per build target and audits
    the union of their graphs. The host needs no Rust tools for any of it.
  - CargoBuildOptions.LicenseGateMode = Enforce (the default), failed
    gate + ThrowOnLicenseViolation (default true): throws the sibling's
    LicensePolicyViolationException (Report holds the audit).
    With ThrowOnLicenseViolation = false: RunCargoAsync returns a result
    with LicenseGateFailed = true and ExitCode -2
    (CargoCommandResult.LicenseGateFailedExitCode); BuildAsync returns a
    result with Success = false, FailedStage = AuditingLicenses, the
    report in LicenseAudit, and one such result per target. cargo never
    runs.
  - LicenseGateMode.ReportOnly audits AND builds in one call: the audit
    runs, ThrowOnLicenseViolation is ignored, and the build goes ahead.
    The report is in LicenseAudit (BuildAsync's result and every target's
    result) whether it passed or not; LicenseGateFailed stays false, a
    failed audit never makes Success false, FailedStage stays null.
    A failed audit is reported as "License audit failed (report only): N
    rejected, M unresolved" - BuildAsync: an AuditingLicenses progress
    report; RunCargoAsync: a CargoProgressPhase.Warning CargoProgress
    (and an AuditingLicenses report on the Progress event).
  - A passed gate: every result's LicenseAudit holds the report.

CRATE TOTAL (CargoProgress.Total)
---------------------------------
  - Total is an ESTIMATE. Unless CargoBuildOptions.ExpectedCrateCount is
    set (then it is used as it is), RunCargoAsync (compiling commands) and
    BuildAsync (per target) count the distinct packages reachable from the
    selected packages over normal and build dependency edges in the
    target's `cargo metadata --filter-platform` graph (the sibling's
    CargoMetadata.GetBuildClosure; dev-dependencies only for the selected
    packages and only for test / all-targets / examples). With a
    LicensePolicy the audit's metadata call serves both; without one the
    `cargo metadata` call is made only when the caller passes a progress
    reporter (Total only feeds progress), and a failed call then only
    leaves Total null - the build still runs and reports the real problem.
    To skip the call altogether, set ExpectedCrateCount.
  - ContainerBuildProgress.Percent follows Completed / Total and is
    clamped to 100.

ARTIFACTS (CodeBrix.RustTools.Docker.Artifacts)
-----------------------------------------------
    public sealed class ArtifactRetrievalOptions
    {
        string HostDirectory { get; set; }          // REQUIRED
        ArtifactLayout Layout { get; set; }         // ByTarget
        bool IncludeExecutables { get; set; }       // true
        bool IncludeSharedLibraries { get; set; }   // true  (.so, .dll)
        bool IncludeStaticLibraries { get; set; }   // false (.a, .dll.a)
        bool IncludeRlibs { get; set; }             // false (.rlib)
        bool Overwrite { get; set; }                // true
        ArtifactRetrievalOptions Clone();
    }
    public enum ArtifactLayout { Flat, ByTarget, NuGetRuntimes }
    public enum ArtifactKind
    {
        Executable, SharedLibrary, StaticLibrary, Rlib, Other,
    }
    public sealed class RetrievedArtifact
    {
        string HostPath, ContainerPath; CrossTarget Target; ArtifactKind Kind;
        long Size; string Sha256;       // 64 lowercase hex, of the host file
        bool IsExecutable;              // an execute bit in the container
        string PackageName, TargetName;
    }
    public sealed class ArtifactRetrievalResult
    {
        IReadOnlyList<RetrievedArtifact> Artifacts; long TotalBytes;
        string HostDirectory;
    }

Where the files go, for sample library "my_lib" built for each target:
    Flat           <host>/libmy_lib.so              (ONE target only)
    ByTarget       <host>/x86_64-unknown-linux-gnu/libmy_lib.so
                   <host>/x86_64-pc-windows-gnullvm/my_lib.dll
                   <host>/aarch64-apple-darwin/libmy_lib.dylib
                   <host>/universal-apple-darwin/libmy_lib.dylib
    NuGetRuntimes  <host>/runtimes/linux-x64/native/libmy_lib.so
                   <host>/runtimes/win-x64/native/my_lib.dll
                   <host>/runtimes/android-arm64/native/libmy_lib.so
                   <host>/runtimes/android-x64/native/libmy_lib.so
                   <host>/runtimes/osx-arm64/native/libmy_lib.dylib
                   <host>/runtimes/osx-x64/native/libmy_lib.dylib
                   <host>/runtimes/osx/native/libmy_lib.dylib   (universal)

  - Every folder is named after the target's own triple and RID.
    LinuxNative is resolved to the engine's own target first, so a default
    build on an ARM64 engine lands in <host>/aarch64-unknown-linux-gnu/ and
    <host>/runtimes/linux-arm64/native/, with RetrievedArtifact.Target =
    LinuxArm64; linux-x64 folders always hold x86-64 files.
  - MacOSUniversal files are joined from the two macOS slice builds (see
    CROSS-COMPILATION TARGETS); osx is the portable macOS RID that
    osx-arm64 and osx-x64 fall back to, so a package may carry either the
    two slices or the one universal file.

  - The files come from the artifacts' Executable and Filenames (container
    paths). Build scripts, test harnesses, .rmeta and .d files are never
    copied; a path reported twice is copied once.
  - Proc-macro and build-script outputs are never retrieved (they are
    host-side compiler plugins); cross-target retrieval takes only the
    target's own output folder (target/<triple>/), never host-compiled
    helper crates under target/<profile>/. Skipped files are not reported.
  - On Linux and macOS hosts the container's permission bits are applied,
    so a retrieved program is executable; Windows hosts record
    IsExecutable only.
  - Overwrite = false and the file exists -> ArtifactRetrievalException
    (ContainerPath names the file); nothing is replaced.
  - A path that no longer exists in the container (for example after
    `cargo clean`) -> ArtifactRetrievalException.

BUILDS (CodeBrix.RustTools.Docker.Builds)
-----------------------------------------
    public sealed class ContainerBuildRequest       // all { get; set; }
    {
        // exactly ONE source:
        string ProjectDirectory;          // package, workspace or member
        string ArchivePath;               // zip / tar / tar.gz / tgz
        ExtractedSource ExtractedSource;  // already extracted (sibling)
        SourceArchiveOptions ArchiveOptions;           // for ArchivePath

        CargoBuildOptions Options;        // sibling type; LicensePolicy
                                          // honoured; TargetTriple and
                                          // WorkingDirectory are replaced
        IList<CrossTarget> Targets;       // [LinuxX64]; built in order
        BuildImageSpec ImageSpec;         // null = base image; the
                                          // targets' layers are added
        BuildContainerOptions ContainerOptions;
        ArtifactRetrievalOptions Retrieval;   // null = retrieve nothing
        bool? DestroyContainerAfterBuild;     // null = remove unless kept
        bool RemoveTargetVolumeAfterBuild;    // true
        bool ContinueOnTargetFailure;         // false = stop at the first
        TimeSpan? Timeout;                    // the whole build
        IList<string> ExtraExcludePatterns;   // added to the defaults
        string CopyRootDirectory;             // null = the Cargo workspace
                                              // root; a wider host folder to
                                              // copy into /work (must contain
                                              // the workspace root; only with
                                              // ProjectDirectory)
        int AndroidApiLevel;                  // 24; 21 or later
        IDictionary<CrossTarget, IDictionary<string, string>>
            TargetEnvironment;                // extra variables for ONE
                                              // target's cargo run (keys must
                                              // be in Targets; MacOSUniversal
                                              // = both slices); per-target
                                              // RUSTFLAGS extend the library's
        ContainerBuildRequest Clone();
    }

    public sealed class ContainerBuildResult
    {
        DockerEngineInfo Engine; RustBuildImage Image;
        string ContainerId, ContainerName; bool ContainerRemoved;
        ContainerBuildSource Source;
        IReadOnlyDictionary<CrossTarget, CargoCommandResult> Results;
        LicenseAuditReport LicenseAudit;          // null without a policy
        ArtifactRetrievalResult Retrieval;        // null without Retrieval
        IReadOnlyDictionary<BuildStage, TimeSpan> StageDurations;
        TimeSpan Duration;
        BuildStage? FailedStage;                  // AuditingLicenses or
                                                  // Building, else null
        bool Success;     // gate passed AND every target built
    }

    public sealed class ContainerBuildSource
    {
        ContainerBuildSourceKind Kind;   // ProjectDirectory | Archive |
                                         // ExtractedSource
        string ArchivePath, HostDirectory, HostManifestPath;
        string ContainerDirectory;       // "/work" or "/work/<member>"
        string PackageName; SourceCopyResult Copy;
    }

    public enum BuildStage
    {
        CheckingEngine, EnsuringImage, StartingContainer, CopyingSource,
        AuditingLicenses, Building, RetrievingArtifacts, CleaningUp,
        Completed, Failed,
    }

    public sealed class ContainerBuildProgress
    {
        BuildStage Stage; string Message;
        CargoProgress CargoProgress;    // only in the Building stage
        double? Percent;                // when the crate total is known,
                                        // and 100 at Completed
        string ToString();              // "[Stage] message" /
                                        // "[Building] [3/10] Compiling ..."
    }

What BuildAsync does, in order (one ContainerBuildProgress per stage
change, image-pull/build line, copied file and cargo report):
  1. CheckingEngine     - DockerUnavailableException (with Guidance) when
                          the engine cannot be used.
  2. EnsuringImage      - ImageSpec plus the layers of the Targets (a
                          Linux target that is the engine's own needs
                          none).
  3. StartingContainer  - with ContainerOptions (ReuseExisting finds a kept
                          container with the same settings).
  4. CopyingSource      - an ArchivePath is extracted on the host first
                          (and deleted after the build); for a workspace
                          MEMBER the whole workspace is copied and cargo
                          runs in /work/<member path>.
  5. AuditingLicenses   - only with Options.LicensePolicy (see LICENSE GATE
                          IN THE CONTAINER).
  6. Building           - `cargo build` per target, in order; stops at the
                          first failed target unless ContinueOnTargetFailure.
  7. RetrievingArtifacts - only with Retrieval, for the targets that
                          succeeded.
  8. CleaningUp         - removes the container (and, by default, its
                          target volume) unless it is kept.
  9. Completed / Failed.
Validation happens BEFORE any engine call: exactly one source, at least one
target and none twice, Retrieval with a HostDirectory, the Flat layout only
for one target (ArgumentException; MacOSUniversal alone counts as one).
MacOSUniversal in Targets builds its two slices (each once, even when they
are named as well) and joins them at retrieval; LinuxNative is resolved to
the engine's own Linux target (once, even when that target is named too). Right after the engine check, an Android target on an
engine that is not x86-64 throws CrossTargetNotSupportedException. When a
stage throws, the container is removed (unless kept), a Failed report is
published, and the exception propagates; a Timeout becomes
ContainerBuildException with an inner TimeoutException.

PACKING NATIVE LIBRARIES INTO A NUGET PACKAGE
---------------------------------------------
Build a cdylib crate for every target with the NuGetRuntimes layout into
one folder, then pack that folder's runtimes/ tree:

    <ItemGroup>
      <None Include="native/runtimes/**" Pack="true"
            PackagePath="runtimes/" />
    </ItemGroup>

NuGet then copies runtimes/<rid>/native/<file> to the consuming
application's output for its runtime identifier, and .NET loads it with
[DllImport("my_lib")] / NativeLibrary.Load. RIDs: linux-x64, linux-arm64,
linux-riscv64, win-x64, android-arm64, android-x64, osx-arm64, osx-x64 and
osx (a universal file, from MacOSUniversal). One engine of any architecture
builds all three Linux RIDs (the two foreign ones through the cross
layers). The macOS files this package makes are development and
testing builds: the macOS libraries of a PUBLISHED package are built ON A
MAC with CodeBrix.RustTools (BuildMacOSUniversalAsync makes the universal
file there), signed and notarised as Apple requires.



CLEANUP
-------
    public sealed class CleanupOptions
    {
        bool RemoveContainers  = true;
        bool RemoveImages      = false;   // derived images only
        bool RemoveVolumes     = false;   // cache AND target volumes
        bool OnlyThisWorkbench = false;   // match <prefix>.workbench too
        bool PruneBuildCache   = false;   // docker builder prune --all --force
                                          // ENGINE-WIDE - read below
    }

    public sealed class CleanupResult
    {
        IReadOnlyList<string> RemovedContainers;   // ids
        IReadOnlyList<string> RemovedImages;       // ids
        IReadOnlyList<string> RemovedVolumes;      // names
        IReadOnlyList<string> Failures;            // one message each
        bool   BuildCachePruned;                   // prune ran and exited 0
        string BuildCacheReclaimedSpace;           // "1.234GB" as printed,
                                                   // null if none
        long?  BuildCacheReclaimedBytes;           // that, as bytes
        string BuildCachePruneOutput;              // raw stdout + stderr
        bool Succeeded { get; }
    }
Only resources with <LabelPrefix>=1 are touched; the base image (rust:...)
never carries the label and is never removed. Containers go first (forced),
so their volumes are free. A volume still used by an unlabelled container
is reported in Failures, not thrown.

RemoveImages does NOT reclaim the BuildKit build cache. Derived images are
built with `docker build` (BuildKit); the cache of their apt, llvm-mingw,
macOS and Android NDK layers - hundreds of MB to several GB per spec -
stays in `docker buildx du` after CleanupAsync(RemoveImages = true,
RemoveVolumes = true) and after `docker rmi`. Cache entries carry no labels
and `docker builder prune --filter` cannot select by label, so the library
cannot remove only its own.

PruneBuildCache = true runs `docker builder prune --all --force` after the other
removals, through the same docker CLI and endpoint that builds the images
(DockerClientOptions.DockerCliPath; DOCKER_HOST set from the endpoint).

    !!! THE PRUNE IS ENGINE-WIDE. It removes EVERY unused BuildKit cache
    !!! entry on the engine (or on the builder the docker CLI selects) -
    !!! this library's AND every other project's - whatever LabelPrefix
    !!! and OnlyThisWorkbench say. Other builds on that engine lose their
    !!! cache. That is why it is off by default; turn it on only when the
    !!! engine is yours to clear (a CI runner, a dedicated build machine)
    !!! or the user has agreed.

A docker CLI that is missing, cannot start or exits non-zero is reported in
Failures (BuildCachePruned = false), not thrown; cancellation is thrown.
BuildCacheReclaimedSpace is the "Total:" value the CLI printed; the bytes
are parsed from it (decimal units are powers of 1000, KiB/MiB/... of 1024).

    var result = await workbench.CleanupAsync(new CleanupOptions
    {
        RemoveImages = true,
        RemoveVolumes = true,
        PruneBuildCache = true,      // engine-wide!
    });
    if (result.BuildCachePruned)
        Console.WriteLine(
            $"Build cache: {result.BuildCacheReclaimedSpace} reclaimed");


EXCEPTIONS
----------
    RustToolsException                    (CodeBrix.RustTools)
      +-- DockerRustToolsException        (base of everything here)
            +-- DockerUnavailableException
            |     DockerEngineInfo Engine;
            |     IReadOnlyList<DockerGuidance> Guidance   // never null
            +-- ContainerBuildException
            |     IReadOnlyList<string> Command;   // null unless a command
            |     long? ExitCode;
            |     string StandardOutput, StandardError;
            +-- ArtifactRetrievalException
            |     string ContainerPath;
            +-- CrossTargetNotSupportedException
                  CrossTarget Target;   // Android on an engine that is
                                        // not x86-64

  - DockerUnavailableException: the engine is unreachable, refused access,
    runs Windows containers, or the endpoint is unusable. Show Guidance.
  - ContainerBuildException: a pull, image build, container start, source
    copy or command (including cargo metadata) failed, the container has
    been removed, or an operation timed out (inner TimeoutException). The
    original CodeBrix.Docker exception is the InnerException; for an image
    build the message ends with the tail of the build's error output.
  - CrossTargetNotSupportedException: an Android target on an engine that is
    not x86-64. (Every CrossTarget value is otherwise supported;
    MacOSUniversal passed to RunCargoAsync is an ArgumentException, since
    it is not a cargo target.)
  - ArtifactRetrievalException: a file to retrieve does not exist in the
    container, exists on the host with Overwrite = false, or cannot be
    written. ContainerPath names it.
  - From the sibling package: LicensePolicyViolationException (failed
    license gate, Report holds the audit) and SourceArchiveException (an
    ArchivePath that cannot be extracted or holds no Cargo.toml).
  - A FAILED COMPILATION IS NOT AN EXCEPTION: the CargoCommandResult (and
    ContainerBuildResult) has Success = false and the diagnostics.
  - ArgumentException / ArgumentNullException: invalid input, thrown before
    any engine call.
  - OperationCanceledException: your token was cancelled (a running cargo
    has been killed inside the container).


COMPLETE EXAMPLES
=================

1. Check the engine and show guidance
-------------------------------------
    using CodeBrix.RustTools.Docker;

    using var workbench = new DockerRustWorkbench();
    var engine = await workbench.CheckEngineAsync();
    if (!engine.IsAvailable)
    {
        Console.WriteLine($"Docker is not usable: {engine}");
        foreach (var step in workbench.GetDockerGuidance(engine))
        {
            Console.WriteLine(step);      // title, explanation, commands, link
            Console.WriteLine();
        }

        return;
    }

    Console.WriteLine($"Docker {engine.ServerVersion}, {engine.Architecture}");

2. Prepare an image with system packages and clippy, run cargo in it
--------------------------------------------------------------------
    using CodeBrix.RustTools.Docker;
    using CodeBrix.RustTools.Docker.Images;

    using var workbench = new DockerRustWorkbench();
    var image = await workbench.EnsureImageAsync(
        new BuildImageSpec
        {
            AptPackages = { "pkg-config", "libssl-dev" },
            InstallClippyRustfmt = true,
        },
        new Progress<string>(line => Console.WriteLine(line)));

    Console.WriteLine(image.WasBuilt ? "built " + image : "reused " + image);

    await using var container = await workbench.StartContainerAsync(image);
    var cargo = await workbench.ExecAsync(container, ["cargo", "--version"]);
    Console.WriteLine(cargo.Trim());      // cargo 1.xx.y (...)

3. Build a project folder through a read-only bind mount
--------------------------------------------------------
The project folder is mounted read-only at /work/src and cargo is pointed
at the target volume, so nothing is written into the host folder.

    using CodeBrix.Docker;
    using CodeBrix.RustTools.Docker;
    using CodeBrix.RustTools.Docker.Containers;

    using var workbench = new DockerRustWorkbench();
    var image = await workbench.EnsureImageAsync();
    await using var container = await workbench.StartContainerAsync(image,
        new BuildContainerOptions
        {
            Mounts = { MountSpec.Bind("/home/me/my-app", "/work/src",
                                      readOnly: true) },
            Env = { ["CARGO_TARGET_DIR"] = "/work/target" },
        });

    try
    {
        await workbench.ExecAsync(container, ["cargo", "build",
            "--manifest-path", "/work/src/Cargo.toml"]);
        var files = await workbench.ExecAsync(container,
            ["ls", "/work/target/debug"]);
        Console.WriteLine(files);
    }
    catch (ContainerBuildException ex)
    {
        Console.WriteLine($"cargo failed ({ex.ExitCode}):");
        Console.WriteLine(ex.StandardError);
    }

Bind mounts need the folder to be shared with the engine (Docker Desktop's
file-sharing settings) and do not work with remote (ssh://, tcp://)
engines. The built files stay in the target volume, inside Docker.

4. Keep a container between runs, then clean up
-----------------------------------------------
    var options = new BuildContainerOptions
    {
        ReuseExisting = true,
        KeepOnDispose = true,
    };
    await using (var c = await workbench.StartContainerAsync(image, options))
    {
        await workbench.ExecAsync(c, ["cargo", "--version"]);
    }   // kept running

    // Later (same or another process): the same settings find it again.
    await using var again = await workbench.StartContainerAsync(image, options);
    Console.WriteLine(again.WasReused);   // True

    // Remove everything the library labelled, including caches and images.
    var result = await workbench.CleanupAsync(new CleanupOptions
    {
        RemoveContainers = true,
        RemoveImages = true,
        RemoveVolumes = true,
    });
    Console.WriteLine($"{result.RemovedContainers.Count} containers, "
        + $"{result.RemovedVolumes.Count} volumes removed");

5. Build a project in one call and bring the program back
--------------------------------------------------------
    using CodeBrix.RustTools.Building;
    using CodeBrix.RustTools.Docker;
    using CodeBrix.RustTools.Docker.Artifacts;
    using CodeBrix.RustTools.Docker.Builds;

    using var workbench = new DockerRustWorkbench();
    var result = await workbench.BuildAsync(
        new ContainerBuildRequest
        {
            ProjectDirectory = "/home/me/my-app",
            Options = new CargoBuildOptions { Profile = CargoProfile.Release },
            Retrieval = new ArtifactRetrievalOptions
            {
                HostDirectory = "/home/me/my-app-out",
                Layout = ArtifactLayout.Flat,
            },
        },
        new Progress<ContainerBuildProgress>(p => Console.WriteLine(p)));

    if (!result.Success)
    {
        foreach (var error in result.Results.Values.SelectMany(r => r.Errors))
        {
            Console.WriteLine(error.Rendered);
        }

        return;
    }

    foreach (var file in result.Retrieval.Artifacts)
    {
        Console.WriteLine($"{file.HostPath} {file.Sha256}");
    }

6. Cross-compile a native library for Windows and Android, NuGet layout
-----------------------------------------------------------------------
    using CodeBrix.RustTools.Docker.Images;

    var result = await workbench.BuildAsync(new ContainerBuildRequest
    {
        ProjectDirectory = "/home/me/my-native-lib",   // crate-type cdylib
        Targets = new List<CrossTarget>
        {
            CrossTarget.LinuxX64,
            CrossTarget.LinuxArm64,
            CrossTarget.WindowsX64Gnullvm,
            CrossTarget.WindowsArm64Gnullvm,
            CrossTarget.AndroidArm64,
            CrossTarget.AndroidX64,
        },
        Retrieval = new ArtifactRetrievalOptions
        {
            HostDirectory = "/home/me/my-package/native",
            Layout = ArtifactLayout.NuGetRuntimes,
            IncludeExecutables = false,
        },
    });
    // native/runtimes/linux-x64/native/libmy_native_lib.so
    // native/runtimes/linux-arm64/native/libmy_native_lib.so  (cross on x64)
    // native/runtimes/win-x64/native/my_native_lib.dll
    // native/runtimes/win-arm64/native/my_native_lib.dll
    // native/runtimes/android-arm64/native/libmy_native_lib.so
    // native/runtimes/android-x64/native/libmy_native_lib.so
The first such build creates the Windows and Android image layers (the
Windows one downloads llvm-mingw, the Android one the NDK, so they take a
while). Android needs an
x86-64 Docker engine; on any other engine BuildAsync throws
CrossTargetNotSupportedException before touching an image. Add
CrossTarget.LinuxX64 to the list for runtimes/linux-x64/native as well.
Then pack the folder as described in PACKING NATIVE LIBRARIES INTO A NUGET
PACKAGE.

7. Gate on licenses, then build and test step by step
-----------------------------------------------------
    using CodeBrix.RustTools;
    using CodeBrix.RustTools.Building;
    using CodeBrix.RustTools.Docker.Artifacts;
    using CodeBrix.RustTools.Licensing;

    var image = await workbench.EnsureImageAsync();
    await using var container = await workbench.StartContainerAsync(image);
    await workbench.CopySourceAsync(container, "/home/me/my-app");

    var audit = await workbench.AuditLicensesAsync(container,
        LicensePolicy.Permissive());
    Console.WriteLine(audit.Passed ? "licenses OK" : audit.ToString());

    var options = new CargoBuildOptions
    {
        LicensePolicy = LicensePolicy.Permissive(),   // gate again, on build
        ThrowOnLicenseViolation = false,
    };
    var build = await workbench.RunCargoAsync(container, CargoCommand.Build,
        options);
    if (build.LicenseGateFailed)
    {
        Console.WriteLine("blocked by the license gate");
    }
    else if (build.Success)
    {
        var tests = await workbench.TestAsync(container);
        Console.WriteLine($"{tests.TestsPassed} passed, "
            + $"{tests.TestsFailed} failed");
        var files = await workbench.RetrieveArtifactsAsync(container,
            build.Artifacts,
            new ArtifactRetrievalOptions { HostDirectory = "out" });
        Console.WriteLine(files);
    }

8. Cancel a long build
----------------------
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try
    {
        await workbench.RunCargoAsync(container, CargoCommand.Build,
            cancellationToken: cts.Token);
    }
    catch (OperationCanceledException)
    {
        // cargo, rustc and the linkers in the container have been killed;
        // the container can be used for the next build.
    }

9. Build macOS slices and a universal library in a container (dev/test only)
-----------------------------------------------------------------------------
    using CodeBrix.RustTools.Docker.Artifacts;
    using CodeBrix.RustTools.Docker.Builds;
    using CodeBrix.RustTools.Docker.Images;

    var result = await workbench.BuildAsync(new ContainerBuildRequest
    {
        ProjectDirectory = "/home/me/my-native-lib",   // pure Rust, cdylib
        Targets = new List<CrossTarget>
        {
            CrossTarget.MacOSArm64,
            CrossTarget.MacOSX64,
            CrossTarget.MacOSUniversal,
        },
        Retrieval = new ArtifactRetrievalOptions
        {
            HostDirectory = "/home/me/out",
            Layout = ArtifactLayout.NuGetRuntimes,
            IncludeExecutables = false,
        },
    });
    // out/runtimes/osx-arm64/native/libmy_native_lib.dylib
    // out/runtimes/osx-x64/native/libmy_native_lib.dylib
    // out/runtimes/osx/native/libmy_native_lib.dylib   (both slices)
    foreach (var file in result.Retrieval.Artifacts)
    {
        Console.WriteLine($"{file.Target}: {file.HostPath}");
    }
The install name defaults to @rpath/libmy_native_lib.dylib for a package
with one cdylib (CargoBuildOptions.MacOSInstallName overrides it). Any
engine architecture works. To get the same bytes from a Mac, export the
sysroot (ExportMacOSSysrootAsync) and build there with
CodeBrix.RustTools.MacOSLinkProfile; see the sibling package's AGENT-README.

10. Inspect a Dockerfile without Docker
----------------------------------------
    var spec = new BuildImageSpec
    {
        CrossTargets = { CrossTarget.WindowsX64Gnullvm },
    };
    Console.WriteLine(DockerfileGenerator.Generate(spec));
    Console.WriteLine(DockerfileGenerator.DerivedImageReference(spec));


MINIMUM VIABLE PROJECT TEMPLATE
===============================
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference
            Include="CodeBrix.RustTools.Docker.MitLicenseForever" />
      </ItemGroup>
    </Project>

    // Program.cs
    using CodeBrix.RustTools.Docker;
    using CodeBrix.RustTools.Docker.Artifacts;
    using CodeBrix.RustTools.Docker.Builds;

    using var workbench = new DockerRustWorkbench();
    var engine = await workbench.CheckEngineAsync();
    if (!engine.IsAvailable)
    {
        foreach (var step in workbench.GetDockerGuidance(engine))
        {
            Console.WriteLine(step);
        }

        return 1;
    }

    var result = await workbench.BuildAsync(new ContainerBuildRequest
    {
        ProjectDirectory = args[0],
        Retrieval = new ArtifactRetrievalOptions { HostDirectory = "out" },
    }, new Progress<ContainerBuildProgress>(p => Console.WriteLine(p)));
    Console.WriteLine(result);
    return result.Success ? 0 : 1;


PERFORMANCE TIPS
================
  1. Keep one DockerRustWorkbench for the life of the application; it owns
     one pooled Docker client.
  2. The first EnsureImageAsync on a machine pulls the base image, a large
     download that can take minutes on a slow connection. Do it early, with
     a progress callback, so users see it happening. Later calls find the
     image on the engine and return at once.
  3. Leave the cargo cache volumes alone between builds (CleanupAsync keeps
     them unless RemoveVolumes is set): dependencies are downloaded once.
  4. Derived images are built once per distinct spec; keep specs stable.
     Every changed character in the generated Dockerfile is a new image.
  5. ReuseExisting + KeepOnDispose keeps a warm container (and its target
     volume, with incremental build output) between runs.
  6. Ask for Android targets only when needed: the first build of that layer
     downloads and unpacks the whole Android NDK (the slowest image step by
     far). Once built, the image is reused like any other derived image,
     and Docker's own build cache also keeps the NDK step for a rebuild.
  7. For repeated builds of one project, keep the container
     (ContainerOptions = new BuildContainerOptions { ReuseExisting = true,
     KeepOnDispose = true }): the target volume keeps cargo's incremental
     state, so a rebuild with unchanged sources is nearly instant. Pass
     DestroyContainerAfterBuild = true on the last build.
  8. Build all targets of a library in ONE BuildAsync call: one image, one
     container, one copy, one license audit.
  9. Keep the excludes tight: everything not excluded is streamed into the
     container on every build. Add large folders the build does not need
     (assets, docs output, data sets) to ExtraExcludePatterns.


COMMON PITFALLS TO AVOID
========================
  1. DO NOT expect the library to install or start Docker. It returns
     guidance; your application shows it.
  2. DO NOT treat CheckEngineAsync as throwing. It reports problems in the
     result; the other operations throw DockerUnavailableException.
  3. DO NOT pass shell syntax as one string to ExecAsync. ["cargo build
     --release"] runs a program literally named "cargo build --release".
     Use ["cargo", "build", "--release"] or ["sh", "-c", "..."].
  4. DO NOT read LinuxX64 as "the container's own target". It is x86-64,
     always: on an ARM64 engine it is a cross build (the first one builds
     the cross layer). Use LinuxNative (the default) for the engine's own
     architecture, and look the result up by the RESOLVED target
     (CrossTargetInfo.NativeLinuxTargetFor(result.Engine)), for example
     result.Results[CrossTarget.LinuxArm64] on Apple Silicon.
  5. DO NOT distribute the macOS files this package makes, and DO NOT ask
     RunCargoAsync for MacOSUniversal (it is joined at retrieval, not
     compiled). They are development and testing builds (see
     CrossTargetInfo.MacOSDevelopmentOnlyNotice); a release build of macOS
     software is made on a Mac with CodeBrix.RustTools.
  6. DO NOT expect derived images to follow a moving base tag. The image tag
     is a hash of the Dockerfile text, which does not change when
     rust:latest does. Pin RustVersion (or the base Tag) for reproducible
     toolchains.
  7. DO NOT dispose the workbench before its containers. Disposal then
     cannot reach the engine and leaves the containers running (their
     labels let CleanupAsync find them later).
  8. DO NOT put CodeBrix.Docker's namespace in a using unless you need
     DockerClientOptions, MountSpec or ResourceLimits; the rest of the API is
     in CodeBrix.RustTools.Docker.*.
  9. DO NOT forget that a derived-image build needs the docker CLI and its
     buildx plugin (DockerEngineIssue.DockerCliNotFound tells you).
 10. DO NOT write build output into a bind-mounted project folder from a
     Linux engine: files created by the container belong to root. Point
     CARGO_TARGET_DIR at /work/target (the target volume) instead - the
     cargo methods of this package always do.
 11. DO NOT treat artifact paths in a container result as host paths. They
     are /work/target/... inside the container; RetrieveArtifactsAsync (or
     BuildAsync with Retrieval) brings the files out.
 12. DO NOT expect Success = false to throw. Compilation errors are in the
     result; only infrastructure problems throw.
 13. DO NOT use the Flat layout for several targets (their files share
     names); BuildAsync rejects it. Use ByTarget or NuGetRuntimes.
 14. DO NOT add to Targets with a collection initializer when you mean to
     replace it: the default list already holds LinuxNative.
 15. DO NOT publish a NuGet package with the macOS native libraries this
     package makes; they are development builds. Build the published ones
     on a Mac with CodeBrix.RustTools (which can also make the universal
     file), where Apple's signing and notarisation are available.
 16. DO NOT start a build without handling DockerUnavailableException when
     the engine may be down (Docker Desktop not started, the daemon stopped,
     the user not in the docker group). Catch it and show its Guidance - or
     call CheckEngineAsync first and show GetDockerGuidance(info).
 17. DO NOT point Retrieval.HostDirectory at a folder that holds files you
     care about. Retrieval writes into it (creating it if needed) and, with
     Overwrite = true (the default), replaces files of the same name. Use a
     dedicated output folder, or Overwrite = false to get an
     ArtifactRetrievalException instead of a silent replacement.
 18. DO NOT expect files outside the source tree, or excluded ones, in the
     container. Only hostDirectory is copied; .git, target, node_modules and
     .DS_Store are skipped at any depth. A build script that runs `git` or
     reads a file you excluded fails inside the container - remove the
     pattern from DefaultExcludePatterns or copy the file in yourself.
 19. DO NOT be surprised by zombie processes after a cancellation. The
     container's main process is `sleep infinity` and the container has NO
     init process, so killed cargo/rustc processes stay listed by `ps` as
     zombies (state Z) until the container is removed. They use no CPU or
     memory, and the container stays usable; remove it to clear them.
 20. DO NOT ask for Android targets on an ARM64 engine (Docker Desktop on
     Apple Silicon, ARM Linux machines). The Android NDK only runs on x86-64
     Linux, so the library throws CrossTargetNotSupportedException; build
     Android libraries on an x86-64 engine.
 21. DO NOT rely on signal handling or child reaping inside a build
     container. There is no init process (no tini, no --init); a program
     you start with ExecAsync that leaves background children behind leaves
     them running until the container is removed.
 22. DO NOT set the plain RUSTFLAGS variable in
     CargoBuildOptions.Environment for a macOS target: cargo lets it
     override the linker flavour, -S, the remapping and the install name
     the macOS layer needs, and the link then fails or the bytes differ.
     Extra flags go in the per-target CARGO_TARGET_<TRIPLE>_RUSTFLAGS,
     which the library appends to the layer's own.
 23. DO NOT expect a cdylib built for macOS to be independent of its build
     path without an install name: set CargoBuildOptions.MacOSInstallName
     (BuildAsync defaults it for a single-cdylib package).
 24. DO NOT build Rust code you do not trust in a kept or shared container.
     Build scripts and procedural macros run arbitrary code at compile time.
     Give untrusted code its own container (a fresh BuildContainerOptions
     Name and TargetVolumeName, ReuseExisting = false), its own cache
     volume (a separate workbench with its own CacheVolumeName), and destroy
     the container and its volumes afterwards (DestroyContainerAfterBuild =
     true, then CleanupAsync with RemoveVolumes and OnlyThisWorkbench).
     Fetch its dependencies first and compile it with NetworkDisabled = true,
     so its build scripts cannot reach the network.
     Never build it on the host. Note that the derived image's BuildKit
     cache is not covered by that cleanup; PruneBuildCache is the only way
     to reclaim it, and it is engine-wide.

 25. DO NOT ask for a foreign platform (BuildImageSpec.Platform =
     "linux/arm64" on an x86-64 engine) without the engine host's qemu-user
     binfmt handler for it: every program of the image then fails with
     "exec format error". Call ProbePlatformAsync first and show its
     Guidance when CanRun is false - and expect the emulated build to be
     many times slower than a native one.
 26. DO NOT pass a comma-separated platform list. One image is one
     platform (ArgumentException); build each platform as its own image.

WHAT THIS PACKAGE DOES NOT DO
=============================
  - Install, update, start or configure Docker, WSL or anything else.
  - Run Windows containers. Windows and macOS are fine as CLIENT machines
    with Docker Desktop in Linux-container mode.
  - Produce macOS binaries that may be distributed or used in production.
    The macOS targets
    (MacOSArm64, MacOSX64, MacOSUniversal) work, on any engine, but READ
    THIS:

  ************************************************************************
  *  IMPORTANT NOTICE: macOS binaries produced by                        *
  *  CodeBrix.RustTools.Docker are built in Docker containers without    *
  *  Apple's SDK, tools or files and are for development and testing     *
  *  only. They must not be distributed or used in any production        *
  *  environment. Any macOS build that will be distributed or run in     *
  *  production must be produced on a Mac and can be created using       *
  *  CodeBrix.RustTools, where Apple's toolchain, SDK and code signing   *
  *  are available under Apple's own license terms.                      *
  ************************************************************************

  - Cross-compile for Android on an engine that is not x86-64 by itself, or
    set up any emulation: foreign platforms (BuildImageSpec.Platform) need
    the engine host's qemu-user binfmt handlers (or Docker Desktop's
    emulation), which this package only probes for and explains.
  - Sign macOS binaries with an identity, or notarise them; the linker's
    ad-hoc signature is all they carry.
  - Build macOS code that compiles C or links an Apple framework beyond
    libSystem.
  - Download a Microsoft SDK or C runtime for Windows builds - Windows output
    uses the GNU toolchain.
  - Include the Android NDK unless an Android target is requested.
  - Run the programs it builds. Retrieve them and run them on a machine of
    the matching platform (or use CodeBrix.RustTools on that machine).
  - Install or call cargo-ndk (see CROSS-COMPILATION TARGETS, Android; add
    it with ExtraDockerfileLines if you want it).
  - Remove anything that does not carry its labels, or the base image.
  - Talk to the Docker engine over TLS (https://); use ssh://.
  - Run on .NET versions below 10.0.


WORKING EXAMPLES ON GITHUB
==========================
The test project exercises every feature against a real Docker engine:
    https://github.com/ellisnet/CodeBrix.RustTools/tree/main/tests/
        CodeBrix.RustTools.Docker.Tests      (one URL, wrapped here)

  Images/DockerfileGeneratorTests.cs     the exact Dockerfile text of every
                                         layer, validation, hashing
  Images/CrossTargetInfoTests.cs         the target table, the x86-64
                                         engine rule for Android
  Engine/DockerGuidanceProviderTests.cs  guidance per OS and issue
  Engine/DockerAvailabilityTests.cs      unreachable / unsupported endpoints
  Engine/DockerPlatformsTests.cs         platform names, machines, lists
                                         rejected
  Engine/PlatformProbeTests.cs           probe outcomes ("exec format
                                         error" = NoEmulator + guidance)
  Images/RustBuildImageBuilderTests.cs   the platform on the pull and on
                                         both image builds
  Containers/BuildContainerFactoryTests.cs  container spec, labels, mounts,
                                         environment, reuse key
  Containers/ContainerCargoRunnerTests.cs  the cargo command line and
                                         environment per target (Android
                                         NDK variables), the kill command
  Artifacts/ArtifactRetrieverTests.cs    which files are retrieved, kinds,
                                         the three layouts
  Builds/ContainerBuildRequestTests.cs   request validation and defaults
  Builds/ContainerBuildPipelineTests.cs  the image layers a request needs,
                                         workspace-member folders
  DockerRustWorkbenchTests.cs            options, validation before the
                                         engine, unreachable-engine guidance
  DockerRustWorkbenchBuildTests.cs       BuildAsync validation, the
                                         engine-down exception with guidance
  Live/DockerRustWorkbenchLiveTests.cs   engine check, base and derived
                                         images, containers, mounts, exec,
                                         reuse, removal, cleanup
  Live/ContainerBuildLiveTests.cs        source copy and excludes, BuildAsync
                                         (retrieval, running the program,
                                         workspace member, archive, kept
                                         container), warnings, license gate,
                                         tests, cancellation, layouts
  Live/WindowsLiveTests.cs               x64 + ARM64 Windows .exe + .dll
                                         (llvm-mingw), NuGet layout
  Live/AndroidLiveTests.cs               arm64 + x64 .so, NuGet layout
  Live/LinuxCrossLiveTests.cs            every Linux architecture but the
                                         engine's own, cross-built
  Live/ForeignPlatformLiveTests.cs       a linux/arm64 Containerfile image
                                         (Architecture arm64) and its
                                         container (uname -m = aarch64) under
                                         qemu-user; the platform probe
  Live/MacOSLiveTests.cs                 arm64 + x64 .dylib, the universal
                                         file, and the byte-for-byte
                                         comparison with a Mac build
  Helpers/DockerEngineFixture.cs         a workbench shared by a test
                                         collection, with cleanup
  Helpers/CrossImageFixture.cs           a cross-compilation image built
                                         once per collection (used by
                                         WindowsFixture, LinuxCrossFixture,
                                         AndroidFixture and MacOSFixture)


QUICK REFERENCE CARD
====================
  Check the engine        await wb.CheckEngineAsync()
  Guidance                wb.GetDockerGuidance(info)
  Base image              await wb.EnsureImageAsync()
  Own Containerfile       await wb.EnsureImageAsync(new BuildImageSpec {
                              Containerfile = "Containerfile" })
  Derived image           await wb.EnsureImageAsync(new BuildImageSpec {
                              AptPackages = {...}, InstallClippyRustfmt = true,
                              CrossTargets = { CrossTarget.WindowsX64Gnullvm } })
  Dockerfile text         DockerfileGenerator.Generate(spec)
  Start a container       await using var c =
                              await wb.StartContainerAsync(image, options)
  Run a command           await wb.ExecAsync(c, ["cargo", "--version"])
  Keep / reuse            new BuildContainerOptions { KeepOnDispose = true,
                              ReuseExisting = true }
  No network              new BuildContainerOptions { NetworkDisabled = true }
                              (fetch first in a networked container)
  Remove one container    await c.StopAndRemoveAsync()
  Remove everything       await wb.CleanupAsync(new CleanupOptions {
                              RemoveImages = true, RemoveVolumes = true })
  Free the build cache    ... PruneBuildCache = true   (ENGINE-WIDE prune;
                              RemoveImages alone never reclaims it)
  Target facts            CrossTargetInfo.Get(CrossTarget.AndroidArm64)
  Engine's own Linux      CrossTargetInfo.NativeLinuxTargetFor(engine)
  Foreign platform image  new BuildImageSpec { Containerfile = ...,
                              Platform = DockerPlatforms.LinuxArm64 }
                              (qemu-user binfmt needed; slow)
  Can it run?             await wb.ProbePlatformAsync("linux/arm64")
                              (.CanRun, .Outcome, .Guidance)
  Linux, every arch       Targets = { CrossTarget.LinuxX64, CrossTarget.LinuxArm64,
                              CrossTarget.LinuxRiscV64 }   (foreign ones cross-built)
  Build in one call       await wb.BuildAsync(new ContainerBuildRequest {
                              ProjectDirectory = dir, Targets = ...,
                              Retrieval = new ArtifactRetrievalOptions {
                                  HostDirectory = "out" } })
  Copy source in          await wb.CopySourceAsync(c, dir)
  Copy beside it          await wb.CopySourceIntoAsync(c, toolDir, "tools/x")
  Wider copy root         new ContainerBuildRequest { ProjectDirectory = crate,
                              CopyRootDirectory = stagingRoot }
  Android at API 33       new ContainerBuildRequest { AndroidApiLevel = 33 }
  One target's variables  request.TargetEnvironment[CrossTarget.WindowsX64Gnullvm]
                              = new Dictionary<string, string> { ... }
  Cargo in the container  await wb.RunCargoAsync(c, CargoCommand.Build,
                              options, CrossTarget.WindowsX64Gnullvm)
  Tests                   await wb.TestAsync(c)
  License audit           await wb.AuditLicensesAsync(c, policy)
  Metadata                await wb.GetMetadataAsync(c)
  Files out               await wb.RetrieveArtifactsAsync(c,
                              result.Artifacts, retrievalOptions, target)
  NuGet native layout     Layout = ArtifactLayout.NuGetRuntimes
  macOS (dev/test only)   Targets = { CrossTarget.MacOSArm64,
                              CrossTarget.MacOSX64, CrossTarget.MacOSUniversal }
  Universal, step by step await wb.RetrieveMacOSUniversalArtifactsAsync(c,
                              arm64.Artifacts, x64.Artifacts, retrievalOptions)
  Sysroot to the Mac      await wb.ExportMacOSSysrootAsync(c, folder)
  Base exception          DockerRustToolsException : RustToolsException

================================================================================
END OF AGENT-README
