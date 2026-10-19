================================================================================
MAINTAINER-README: CodeBrix.RustTools
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING a NuGet package, stop reading and open its AGENT-README
instead (README-INDEX.txt lists them). Everything below is about the
repository itself: how it is laid out, how it builds, how it is tested, how
it is packaged, and the conventions the source follows.


PURPOSE AND SCOPE
=================
This repository produces three NuGet packages, built from one solution:

    PackageId:  CodeBrix.RustTools.MitLicenseForever
    Assembly:   CodeBrix.RustTools
    Project:    src/CodeBrix.RustTools/CodeBrix.RustTools.csproj
    License:    MIT
    Consumer documentation: AGENT-README.txt (repo root)

    PackageId:  CodeBrix.RustTools.Docker.MitLicenseForever
    Assembly:   CodeBrix.RustTools.Docker
    Project:    src/CodeBrix.RustTools.Docker/CodeBrix.RustTools.Docker.csproj
    License:    MIT
    Consumer documentation: src/CodeBrix.RustTools.Docker/AGENT-README.txt

    PackageId:  CodeBrix.RustTools.CrateBuilder.MitLicenseForever
    Assembly:   CodeBrix.RustTools.CrateBuilder
    Project:    src/CodeBrix.RustTools.CrateBuilder/
                CodeBrix.RustTools.CrateBuilder.csproj (one path, wrapped)
    License:    MIT
    Consumer documentation:
                src/CodeBrix.RustTools.CrateBuilder/AGENT-README.txt

CodeBrix.RustTools uses the Rust tools that are ALREADY INSTALLED on the
machine (Windows, macOS, Debian-based Linux) to inspect, license-check, build
and run Rust applications from .NET. It never installs anything: when a tool
is missing or outdated it returns operating-system-specific guidance (data)
for the consumer to show. Its only NuGet dependency is
CodeBrix.TomlParse.BsdLicenseForever; HTTP, JSON, process and archive
handling use the BCL.

CodeBrix.RustTools.Docker does the same work inside a Linux Docker container,
so the host needs a Docker engine and no Rust tools. It checks the engine
(returning per-OS Docker guidance, never installing or starting anything),
generates deterministic Dockerfiles for build images (official `rust` image
plus optional layers: apt packages, clippy/rustfmt, a pinned toolchain,
Windows (llvm-mingw), Linux cross and Android cross-compilation), starts
labelled build containers
with persistent cargo cache volumes, copies the source in, applies the
license gate and runs cargo inside the container with the CodeBrix.RustTools
result model, copies artifacts out (flat, per target or in the NuGet
runtimes/<rid>/native layout), does all of it in one BuildAsync call and
cleans up what it created. Its dependencies are the CodeBrix.RustTools
project (ProjectReference, so the package depends on the CodeBrix.RustTools
package built in the same build) and CodeBrix.Docker.MitLicenseForever
(pinned PackageReference). Files travel through the archive (copy-in /
copy-out) operations of CodeBrix.Docker, behind the internal
IContainerFileTransport interface (implementation: DockerCopyTransport), so
no shared folders are needed and remote engines work. macOS targets are
rejected; see the PHASE 3 section at the end for the planned macOS
cross-build work that belongs to this package.

CodeBrix.RustTools.CrateBuilder turns a Rust crate (a local Cargo.toml, or a
Git repository cloned with CodeBrix.LibGit2) into native binaries for chosen
runtime identifiers - built with CodeBrix.RustTools on the host or with
CodeBrix.RustTools.Docker in a container - plus a JSON build record and,
optionally, a generated C facade and a .NET wrapper class library. Its
dependencies: ProjectReferences to both sibling projects, and pinned
PackageReferences to CodeBrix.LibGit2.MitLicenseForever,
CodeBrix.WebGpu.Native.MitLicenseForever and
CodeBrix.Templating.BsdLicenseForever. See the CRATEBUILDER section below.


REPOSITORY LAYOUT
=================
    src/CodeBrix.RustTools/          the CodeBrix.RustTools library project
                                     (packable)
      (root)                         RustWorkbench (facade),
                                     RustWorkbenchOptions, RustToolsException
                                     and its four derived exceptions,
                                     InternalsVisibleTo.cs
      Toolchain/                     tool detection, ToolVersion,
                                     RustToolchainFile, HostOperatingSystem,
                                     readiness, InstallGuidanceProvider
      Registry/                      RustReleaseClient (static.rust-lang.org
                                     manifests), CratesIoClient,
                                     ToolUpdateReport
      Projects/                      CargoProject, CargoManifest (+ package,
                                     dependency, feature, workspace, target
                                     and profile models), CargoLockFile,
                                     CargoMetadata, CargoProjectLocator
      Licensing/                     OpenSourceLicense (SPDX list),
                                     SpdxLicenseExpression, LicensePolicy,
                                     LicenseAuditor, LicenseAuditReport
      Dependencies/                  DependencyInspector, DependencyReport
      Building/                      CargoRunner, CargoBuildOptions,
                                     CargoCommandResult / CargoTestResult,
                                     diagnostics, artifacts, progress;
                                     internal JSON / output parsing;
                                     MacOSLinkProfile, MacOSUniversalBinary
                                     (+ MacOSBinarySlice, MacOSUniversalFile,
                                     MacOSUniversalBuildResult);
                                     AndroidLinkProfile and the
                                     BuildAndroidAsync options / result /
                                     file / layout types; LinuxLinkProfile
                                     and the BuildLinuxAsync options /
                                     result / file / layout types
                                     (LinuxBuildOptions, LinuxBuildResult,
                                     LinuxBuildFile, LinuxOutputLayout);
                                     WindowsGnullvmLinkProfile
      Running/                       BuiltBinaryRunner, RunOptions, RunResult
      Archives/                      SourceArchiveExtractor, ExtractedSource,
                                     ExtractAndBuildResult
      Internal/                      internal helpers: ProcessRunner (+
                                     request/result), ExecutableLocator,
                                     HttpClientSource, TomlHelpers

    src/CodeBrix.RustTools.Docker/   the CodeBrix.RustTools.Docker library
                                     project (packable); its README.md and
                                     AGENT-README.txt live beside the csproj
      (root)                         DockerRustWorkbench (facade),
                                     DockerRustWorkbenchOptions,
                                     DockerRustTimeouts, CleanupOptions /
                                     CleanupResult, DockerRustToolsException
                                     and its four derived exceptions,
                                     InternalsVisibleTo.cs
      Engine/                        DockerAvailability, DockerEngineInfo,
                                     DockerEngineIssue, DockerGuidance,
                                     DockerGuidanceProvider
      Images/                        BuildImageSpec, DockerfileGenerator,
                                     RustBuildImage, CrossTarget,
                                     CrossTargetInfo; internal
                                     RustBuildImageBuilder
      Containers/                    BuildContainer, BuildContainerOptions,
                                     SourceCopyProgress, SourceCopyResult;
                                     internal BuildContainerFactory,
                                     ContainerCommandRunner (+ result),
                                     ContainerCargoRunner (+ invocation),
                                     ContainerMetadataRunner and the
                                     IContainerFileTransport interface with
                                     its DockerCopyTransport implementation
                                     (+ TransportedFile, ContainerPathStat)
      Artifacts/                     ArtifactRetrievalOptions,
                                     ArtifactLayout, ArtifactKind,
                                     RetrievedArtifact,
                                     ArtifactRetrievalResult; internal
                                     ArtifactRetriever
      Builds/                        BuildStage, ContainerBuildProgress,
                                     ContainerBuildRequest,
                                     ContainerBuildResult,
                                     ContainerBuildSource(+Kind); internal
                                     ContainerBuildPipeline
      Internal/                      ResourceLabels, LineSplitter, Hashing

    src/CodeBrix.RustTools.CrateBuilder/
                                     the CodeBrix.RustTools.CrateBuilder
                                     library project (packable); its
                                     README.md and AGENT-README.txt live
                                     beside the csproj
      (root)                         CrateBuilder (facade),
                                     CrateBuilderOptions,
                                     CrateBuilderException and its five
                                     derived exceptions, InternalsVisibleTo.cs
      Targets/                       BuildMode, CrateTarget, CrateTargets,
                                     LinuxBaseline, CrateBuildProfile,
                                     ArtifactProvenance, CrateTargetOutcome
      Requests/                      CrateBuildRequest, CrateSource,
                                     LocalManifestSource, GitRepositorySource,
                                     WrapperOptions, WrapperPackaging,
                                     WrapPlan, FacadeOverride, FacadeMode
      Results/                       CrateBuildStage, CrateBuildProgress,
                                     CrateReadinessReport, BuildReadinessIssue,
                                     CrateSourceIdentity, CrateTargetResult,
                                     WrapperResult, CrateBuildResult
      Records/                       BuildRecord, TargetRecord,
                                     ArtifactRecord, WrapperRecord,
                                     FacadeRecord
      Internal/Pipeline/             the stage seams, their data types, the
                                     run context, CrateBuilderStages
                                     (CreateDefault wires the real stages)
                                     and CrateBuildPipeline (see
                                     CRATEBUILDER)
      Internal/Readiness/            ReadinessChecker and the host / Docker
                                     target evaluators
      Sources/                       SourceResolver (local + LibGit2 clone /
                                     fetch / checkout, reuse rule),
                                     CrateManifestFacts, GitRemoteUrl
      Staging/                       WorkspaceStager (copy to
                                     <run>/stage/<package>/src, vendoring to
                                     .codebrix-vendor/, path rewrites),
                                     WorkspaceRootFinder (cargo's rule),
                                     ManifestDependencyScanner,
                                     ManifestPathRewriter, SourceHasher,
                                     StagingFileSystem, StagedPathRewrite
      Facade/                        FacadeGenerator (the IFacadeGenerator:
                                     extraction -> planning -> rendering)
        Extraction/                  ApiExtractor and the embedded
                                     codebrix-api-extractor source (Tool/)
        Model/                       CrateApiModel (the extractor's JSON)
        Planning/                    MappingPlanner, PlannerRun, the type
                                     table, WrapReportWriter; Model/ = the
                                     MappingPlan the renderers walk
        Rendering/                   FacadeRenderer, FacadeSourceBuilder,
                                     FacadeConventions, Templates/*.sbn
      Building/                      ModeBuildDriver (the IBuildDriver),
                                     HostBuildDriver, DockerBuildDriver,
                                     TargetMatrix, contributions,
                                     CargoOptionsFactory, ManyLinux/
                                     (Containerfile.x86_64), Verification/
                                     (NativeArtifactReader, the verifiers,
                                     VerificationRecorder)
      WebGpu/                        NativePackageLocator, WebGpuLinkStager
                                     (IBuildContribution), WebGpuLinkVerifier,
                                     WebGpuLinkRecipe; Internal/ = the
                                     ELF / PE / Mach-O link readers
      Wrapper/                       WrapperProjectWriter (IWrapperWriter);
                                     Generation/ (C# member emitter, type
                                     spellings, model builder), Model/ (template
                                     model), Rendering/ (template loader,
                                     generated-files.json), Templates/*.sbn

    tests/CodeBrix.RustTools.Tests/  the xUnit v3 test project; one folder
                                     per source folder, plus the facade
                                     tests (RustWorkbench*Tests.cs) at its
                                     root
      Helpers/                       RepoPaths, SampleSandbox,
                                     CargoAvailability, LiveNetwork,
                                     CollectingProgress,
                                     StubHttpMessageHandler, TestToolchains
      Fixtures/                      captured cargo / rustc / libtest output
                                     and trimmed release-manifest and
                                     crates.io payloads (copied to the
                                     output folder)

    tests/CodeBrix.RustTools.Docker.Tests/
                                     the Docker package's xUnit v3 test
                                     project: pure unit tests in folders
                                     mirroring the source, the live
                                     real-Docker tests in Live/; Helpers/
                                     holds the four collection fixtures
                                     (DockerEngineFixture, WindowsFixture,
                                     AndroidFixture, MacOSFixture; the last
                                     three share CrossImageFixture), the collection
                                     definitions, SynchronousProgress and
                                     copies of RepoPaths / SampleSandbox

    tests/CodeBrix.RustTools.CrateBuilder.Tests/
                                     the CrateBuilder package's xUnit v3
                                     test project, in folders mirroring the
                                     source; Helpers/ holds copies of
                                     RepoPaths / SampleSandbox /
                                     SynchronousProgress, Capabilities
                                     (the cargo and Docker skips) and
                                     CCompiler (the C header checks);
                                     EndToEnd/ holds the end-to-end tests
                                     through the public CrateBuilder

    rust-samples/                    seventeen small Rust projects
                                     (test_project_01 ... 13, 15 ... 18)
                                     used as read-only test inputs; 13 is
                                     the cdylib used by the cross-compilation
                                     tests, 15 - 18 are the CrateBuilder
                                     inputs (EXTRAS-README.txt)
    tools/macos-cross/               Phase 3 notes (where the stub derivation
                                     lives, how to inspect a stub) and a
                                     Mac-side check script; maintainer-only,
                                     not in the solution, not packaged
                                     (EXTRAS-README.txt)

    CodeBrix.RustTools.slnx          the solution; its Solution Items folder
                                     carries .gitignore, AGENT-README.txt,
                                     EXTRAS-README.txt, global.json,
                                     icon-codebrix-128.png, LICENSE,
                                     MAINTAINER-README.txt, README-INDEX.txt,
                                     README.md and THIRD-PARTY-NOTICES.txt;
                                     its Tests folder carries the three test
                                     projects; the three library projects sit
                                     at the solution root. rust-samples/ and
                                     tools/ are deliberately NOT in the
                                     solution.

    global.json                      selects the Microsoft.Testing.Platform
                                     test runner. Does NOT pin an SDK version.
                                     See TESTING below.

Source folders map to namespaces: CodeBrix.RustTools.Toolchain, .Registry,
.Projects, .Licensing, .Dependencies, .Building, .Running, .Archives and
.Internal (internal types only); and CodeBrix.RustTools.Docker.Engine,
.Images, .Containers, .Artifacts, .Builds and .Internal. The one exception
is CodeBrix.RustTools.CrateBuilder: every PUBLIC type is in the root
namespace CodeBrix.RustTools.CrateBuilder (one using for consumers), while
the files are grouped in the Targets/, Requests/, Results/ and Records/
folders; its internal types follow their folders
(CodeBrix.RustTools.CrateBuilder.Internal.Pipeline, .Internal.Readiness,
.Sources, .Staging, .Facade.*, .Building.*, .WebGpu.*, .Wrapper.*).


BUILDING
========
    dotnet restore CodeBrix.RustTools.slnx
    dotnet build --configuration Release CodeBrix.RustTools.slnx

The build must report 0 warnings and 0 errors in both Debug and Release,
including the xUnit analyzer warnings of both test projects. Target framework:
net10.0 only. GenerateDocumentationFile is ON, so every public member must
carry an XML doc comment; fix CS1591 at the source and never add a
project-wide <NoWarn>.


TESTING
=======
    dotnet test --solution CodeBrix.RustTools.slnx

THE TEST RUNNER IS Microsoft.Testing.Platform (MTP), selected by global.json at
the repo root:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

Keep that file - without it `dotnet test` falls back to the VSTest bridge,
which fails on the .NET 10 SDK. MTP output ends in a "Test run summary:"
block. If `dotnet test --solution` ever reports zero tests, run the test
assemblies directly instead:

    dotnet tests/CodeBrix.RustTools.Tests/bin/Debug/net10.0/
        CodeBrix.RustTools.Tests.dll        (one path, wrapped here)
    dotnet tests/CodeBrix.RustTools.Docker.Tests/bin/Debug/net10.0/
        CodeBrix.RustTools.Docker.Tests.dll (one path, wrapped here)

All three test projects are xUnit v3 with SilverAssertions; no coverage
collector is referenced. Each has an InternalsVisibleTo grant from its
library.

Sandbox strategy:
  * Tests NEVER run cargo inside rust-samples/. SampleSandbox.FromSample(n)
    copies rust-samples/test_project_NN (skipping target/ and hidden
    folders) to bin/.../sandboxes/<guid>/ under the test output folder, and
    deletes it on Dispose. Archives and extraction folders made by the
    archive tests live next to the sandbox copy, so they go with it.
  * Read-only parsing tests (Cargo.toml, Cargo.lock, locator) may read the
    samples in place; they never write there.
  * After a run, rust-samples/ must contain no target/ or output/ folder and
    bin/.../sandboxes/ should be empty.

Finding the repository: RepoPaths resolves the repo root from, in order, the
environment variable CODEBRIX_RUSTTOOLS_REPO_ROOT, the helper's own source
path (CallerFilePath, so builds with --artifacts-path work), then by walking
up from the test output folder.

Prerequisites and gating:
  * Tests that run the real Rust tools call CargoAvailability.SkipIfMissing()
    and are dynamically skipped (Assert.Skip) when cargo is not on PATH or in
    ~/.cargo/bin. They never install anything (every process runs with
    RUSTUP_AUTO_INSTALL=0).
  * Tests that run cargo on a sample with registry dependencies use Locked
    (the sample's Cargo.lock exactly as it is) and NOT Offline, so cargo
    downloads whatever the local cache (CARGO_HOME) does not hold yet. On
    a machine with a cold cache the first run downloads the crates of
    samples 02, 04, 05 and 07 (about 50) from crates.io; later runs find
    them cached. They never skip for a cold cache. TestMetadata
    .GetMetadataAsync is the shared `cargo metadata` call (Locked, skips
    only when cargo is missing).
  * Offline itself is covered by tests that fill the cache first:
    CargoRunnerTests.BuildAsync_builds_real_dependencies_offline_... runs
    FetchAsync and then an Offline build, and RustWorkbenchFetchTests
    fetches sample 02 into an EMPTY CARGO_HOME of its own (a short folder
    under the system temp folder, deleted afterwards) and then runs
    `cargo metadata` offline against it - so it proves the fetch even
    when the machine's cache is warm, at the cost of downloading sample
    02's crates (about 30) on every run.
  * To check the suite against a cold cache without emptying your own,
    run it with CARGO_HOME pointing at an empty folder (for example
    `CARGO_HOME=/tmp/empty-cargo-home dotnet test --project
    tests/CodeBrix.RustTools.Tests/CodeBrix.RustTools.Tests.csproj`);
    rustup still finds the toolchains through RUSTUP_HOME.
  * The library's OWN requests to static.rust-lang.org and crates.io
    (RustReleaseClient, CratesIoClient) are tested live only when
    CODEBRIX_RUSTTOOLS_LIVE_TESTS=1. Everything else uses
    StubHttpMessageHandler and the files in Fixtures/. (cargo's crate
    downloads above are not gated by that variable.)
  * The process-runner tests use /bin/sh and skip on Windows; the
    process-tree kill test reads /proc and runs on Linux only.
  * RustWorkbenchMacOSUniversalTests run on a Mac: the universal build of
    sample 13 needs both Darwin targets installed (`rustup target add
    x86_64-apple-darwin` on Apple Silicon, aarch64-apple-darwin on Intel)
    and skips otherwise; the guidance test runs only when one is missing.
  * RustWorkbenchAndroidTests build sample 13 for both Android ABIs with the
    host's NDK: they skip unless an NDK is found (set ANDROID_NDK_HOME to the
    NDK folder or ANDROID_HOME to the SDK folder that holds ndk/<version>;
    a variable exported only by a login shell is not seen by a test runner
    started from an IDE or a tool, so pass it on the command line, for
    example `ANDROID_HOME=<sdk> dotnet test ...`) and both targets are
    installed (`rustup target add aarch64-linux-android x86_64-linux-android`).
    The wrong-path guidance test runs anywhere cargo is.

Which samples the CodeBrix.RustTools suite builds: 02 (one
real-dependencies build), 04
(workspace, GPL member), 05 (lib + bin, features, vendored license
fixtures - the workhorse), 06 (build script, two bins) and 10 (warnings).
07 is used for `cargo metadata` only (the transitive MPL-2.0 crate). 03, 08,
09 and 11 are never built by the tests: they pull in TLS, async runtimes,
bundled SQLite (C) or image codecs and take minutes to compile. 01 and 12
are not used by the tests.

The Docker suite (tests/CodeBrix.RustTools.Docker.Tests):
  * REAL DOCKER, NO GATE. The live tests talk to the local Docker engine and
    FAIL (they never skip) when it is not running, not reachable or not in
    Linux-container mode - the collection fixture's failure message carries
    the engine check and the guidance. Only the two endpoint unit tests that
    use a Unix socket path skip on Windows.
    The one deliberate SKIP: the Android collection on an engine that is not
    x86-64 (Docker Desktop on Apple Silicon, ARM Linux). The NDK cannot run
    there, so the library refuses the targets; CrossImageFixture catches that
    CrossTargetNotSupportedException instead of building the image, records
    it in UnsupportedReason, and every test of the collection calls
    SkipIfUnsupported() first, so it is reported as skipped with the library's
    own message. An engine that cannot run the tools is not a broken test
    environment, unlike a missing engine.
  * THREE collections, each with its own fixture and its own workbench,
    so the slow image layers are built once per run and the collections
    can run in parallel without sharing a cache volume:
      "Docker engine"             DockerEngineFixture: test names (cache
                                  volume codebrix-rusttools-tests-cargo-cache,
                                  image repository and container prefix
                                  codebrix-rusttools-tests), the engine
                                  check, rust:latest (pulled on the first
                                  run on a machine - a large download), one
                                  shared build container. Most live tests
                                  live here.
      "Docker Windows image"      WindowsFixture: the derived image with the
                                  Windows layer - llvm-mingw for the
                                  engine's architecture plus both
                                  windows-gnullvm targets (cache volume
                                  ...-cargo-cache-windows). Builds sample
                                  13 for x64 and ARM64 Windows and checks
                                  each PE's machine type (0x8664, 0xAA64),
                                  that no libunwind.dll is imported and
                                  that the UCRT is; on a Windows x64 host
                                  it also runs the x64 program.
      "Docker Android image"      AndroidFixture: the derived image with the
                                  Android layer, which downloads the
                                  Android NDK (cache volume
                                  ...-cargo-cache-android). Needs an
                                  x86-64 engine: on an ARM64 engine the
                                  library refuses Android targets, so the
                                  fixture builds nothing and the
                                  collection's tests SKIP (see above).
      "Docker Linux cross image"  LinuxCrossFixture: the derived image with
                                  the cross layers of the two Linux
                                  architectures the engine is not (about a
                                  minute: two Debian cross toolchains plus
                                  two rustup targets; cache volume
                                  ...-cargo-cache-linux-cross). Builds
                                  sample 13 for both into the NuGet layout
                                  and checks each ELF's machine type.
      "Docker macOS image"        MacOSFixture: the derived image with the
                                  macOS layer (both Darwin targets,
                                  llvm-tools, the libSystem stub derived at
                                  image build; about 40 s; cache volume
                                  ...-cargo-cache-macos). Works on x64 and
                                  ARM64 engines. Builds sample 13 for both
                                  slices and as a universal file, links an
                                  ad-hoc cdylib that uses pow, ln, sin_cos
                                  and % (the C math calls the stub must
                                  name) for both slices, and - on a Mac
                                  with cargo - builds sample 13 on the HOST
                                  too, with CodeBrix.RustTools and
                                  MacOSLinkProfile against the exported
                                  sysroot, and asserts the SHA-256s of the
                                  program and the dylib match the
                                  container's (skips off macOS or without
                                  cargo).
    Each fixture FAILS its collection when the engine cannot be used, and
    at the end removes every container, derived image and volume its own
    workbench created. Every fixture and every slow build appends a line
    with its duration to docker-fixture-log.txt in the test output folder.
  * Expected times: the first run on a machine is dominated by the
    rust:latest pull and the cross images (the Windows layer downloads
    llvm-mingw, about 80 MB; the Android layer downloads and unpacks the NDK,
    the slowest step) - a few minutes in all on a good connection. Once
    Docker's build cache is warm the images rebuild in seconds, the cargo
    builds themselves take seconds, and a whole Docker suite run is short.
    Actual durations of every fixture and slow build are appended to
    docker-fixture-log.txt in the test output folder; read that file
    rather than trusting numbers written here.
  * CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1 keeps the cargo cache volumes and
    the derived test images (clippy + rustfmt, Windows, Linux cross, Android, macOS)
    between runs. It is a SPEED knob only; the suite runs the same tests
    either way.
  * Images the suite builds: five derived images - clippy + rustfmt,
    Windows (llvm-mingw), Linux cross, Android, macOS. The Dockerfile text
    of every layer is also pinned by golden-text unit tests (the macOS
    layer's script and the llvm-mingw URLs and hashes included).
  * Samples the Docker suite builds (in sandbox copies, never in
    rust-samples/): 02 (progress, kept-container rebuild, cancellation),
    04 (license gate inside the container, a workspace member), 05 (build,
    retrieval, running the retrieved binary on a Linux host of the engine's
    architecture, tests,
    zip archive), 06 (two executables), 10 (warnings), 13 (removal,
    retrieval layouts, Windows, Linux, Android and macOS cross-builds, the
    universal file, the Mac identity check). Never 03, 08, 09 or 11.
  * The cancellation test cancels a build of 02 at its third "Compiling"
    report and then lists the container's processes with `ps`: no live
    cargo, rustc, build script or linker may remain. Killed processes can
    stay listed as zombies (state Z) because the container's main process
    (sleep) does not reap orphans; the test ignores those, and they vanish
    with the container.
  * The base image (rust:latest) is never removed by the suite.
  * FOREIGN PLATFORMS (Live/ForeignPlatformLiveTests.cs, "Docker engine"
    collection): a Containerfile image `FROM alpine:latest` built with
    Platform linux/arm64 (inspect Architecture arm64; its tag differs from
    the same spec's without a platform; a second EnsureImageAsync reuses it),
    a build container from it (`uname -m` = aarch64, BuildContainer.Platform),
    the platform probe (Runs for linux/arm64, cached, Native for the engine's
    own, no probe container left behind) and a probe of linux/sparc64, which
    busybox does not publish (Refused). Each test SKIPS BY NAME when the
    engine is arm64 itself or /proc/sys/fs/binfmt_misc/qemu-aarch64 is
    absent. The "no binfmt handler" outcome (NoEmulator) cannot be produced
    live on a machine that has every common qemu-user handler registered
    (this laptop has qemu-aarch64, qemu-riscv64, qemu-arm, qemu-ppc64le,
    qemu-s390x and more) without changing the host, so its classification
    and guidance are unit-tested (Engine/PlatformProbeTests.cs). No Rust
    image is built for a foreign platform here; CrateBuilder's ManyLinux
    tests do that. The pulls leave the linux/arm64 variants of alpine:latest
    and busybox:latest on the engine (base images: never removed; on the
    containerd image store they are variants of the same tags). The images
    the tests build carry the fixture's labels and are removed with the
    others.
  * Hygiene after a run (unless the KEEP variable is set): these list
    nothing -
        docker ps -a --filter label=codebrix.rusttools.docker=1
        docker volume ls --filter label=codebrix.rusttools.docker=1
        docker images --filter label=codebrix.rusttools.docker=1
  * The pure unit tests (Dockerfile golden text, the target table, options,
    guidance, container spec and reuse key, line splitting, the container
    cargo command line and environment, artifact selection and layouts,
    request validation) need no Docker.
  * DockerfileGeneratorTests pins the hash of the default Dockerfile. A
    deliberate change to the generated text must update the pin - and it
    means every derived image built before the change is rebuilt.

The CrateBuilder suite (tests/CodeBrix.RustTools.CrateBuilder.Tests):

    dotnet test --project tests/CodeBrix.RustTools.CrateBuilder.Tests

  * Unit tests need nothing: the target table, request and wrapper
    validation, the build record's JSON, the exceptions, the stage order and
    failure mapping (fake stages through the internal CrateBuilder
    constructor), and the readiness mapping from constructed toolchain and
    engine data (fake tool files in sandboxes).
  * Live tests SKIP BY NAME (Helpers/Capabilities and the per-area helpers
    below) when cargo / rustc, a usable Docker engine, the dotnet CLI, the
    CodeBrix.WebGpu.Native package, a cross toolchain or the network is
    missing - never a passing stub. None of them installs anything.
  * Same sandbox rule: never run cargo inside rust-samples/; the host
    readiness test reads a SampleSandbox copy of sample 13.
  * Sources/ and Staging/ tests clone LOCAL upstream repositories made with
    CodeBrix.LibGit2 (file:// URLs, no network) and stage sandbox copies of
    sample 18. Live: one clone of https://github.com/ellisnet/CodeBrix.RustTools
    (skips without a route to github.com:443) and one RustWorkbench test of
    the staged sample 18 with a vendored path dependency (skips without cargo).
    Submodules: a local superproject records a second local repository as a
    submodule (TreeDefinition.AddGitLink + .gitmodules); the clone holds it
    only with RecurseSubmodules, also when it appears upstream after a clone
    is reused. The credentials callback is passed through but not exercised:
    file:// remotes never ask for credentials.
  * Build-driver live tests build sample 13 (cdylib, no dependencies) in
    sandbox copies. Host: linux-x64, linux-arm64 / linux-riscv64 (Debian
    cross gcc), both Android ABIs (NDK via ANDROID_NDK_HOME / ANDROID_HOME),
    macOS through the link profile only when CODEBRIX_CRATEBUILDER_MACOS_SYSROOT
    names an exported sysroot, Windows only with the gnullvm targets and
    llvm-mingw; each skips with the readiness check's own reason. Docker
    (skip without an engine; x86-64 engine): linux-x64 + win-x64 in the rust
    image, and ONE ManyLinux build of linux-x64 + win-x64 (linux-x64 built
    in the manylinux_2_28 image - pulled and built with rustup the first
    time, about 1.5 minutes -, win-x64 NotBuildableInThisMode: ManyLinux is
    Linux only). The emulated ManyLinux targets are covered by EndToEnd/. The
    fixture's workbench uses label prefix codebrix.rusttools.cratebuilder.tests
    and removes its containers, volumes and derived images
    (CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1 keeps them). Durations go to
    build-driver-log.txt.
  * NativeArtifactReader tests read copies of the nine CodeBrix.WebGpu.Native
    binaries from the NuGet cache (skip if the package is not restored),
    including the Android API level of the two Android engine binaries (33).
  * WebGpu/: the locator, reader and verifier tests read the
    CodeBrix.WebGpu.Native package of the NuGet cache (copies in sandboxes)
    and skip by name when it is absent. WebGpuLinkLiveTests build sample 16
    (host triple; linux-arm64 with the cross gcc; android-arm64 at API 33
    with an NDK - each skips by name without its toolchain), verify the link,
    and on the host load the module AFTER CodeBrixWebGpuNative.EnsureLoaded(),
    call probe_api_version (== 2) and check /proc/self/maps holds exactly one
    libcodebrix_wgpu.so. The "does not reference the engine" warning path is
    tested on the .NET runtime's own libSystem.Native.so (Linux only).
  * The API extractor's live tests build the tool once into
    tests/.../bin/<configuration>/net10.0/cratebuilder-scratch/ (kept between
    runs) and extract sandbox copies of samples 15, 17 and 02; they skip by
    name without cargo. The Docker extraction tests (ApiExtractorDockerTests,
    an isolated workbench labelled codebrix.rusttools.cratebuilder.tests.extract)
    build the tool in a container from a fresh scratch folder - run inside
    the container, retrieved and run on a Linux host of the engine's
    architecture, and the fallback to the container when the retrieved
    program cannot run - and skip by name without a usable engine. The
    in-container route copies the tool and the staged crate side by side
    (CopySourceIntoAsync: /work/codebrix-api-extractor and /work/crate), so
    nothing relies on the tool's binary surviving a later copy. Their
    fixture removes its containers and volumes at the end unless
    CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1.
  * Facade/Planning/ tests plan the checked-in samples in Facade/Model/samples/
    (no cargo); the two live rows re-extract samples 15 and 17 with the
    host-built extractor and skip by name without cargo.
  * Facade/Rendering/ tests render hand-built plans (Fixtures/) and the
    planner's plan of the datacrate sample (no cargo); the live tests stage
    sample 15 and a small crate the test writes into its sandbox
    (CoverageCrate: every shape), build the facades with RustWorkbench
    (release, offline, no warnings allowed), check the exports with
    NativeArtifactReader and call them through NativeLibrary; one builds a
    WebGPU-linked facade with the link stager. They skip by name without
    cargo (and without the WebGpu.Native package for that one). The header
    tests render the facades of the three model samples and CoverageCrate and
    compile each <lib>.h, included twice, with the machine's C compiler (CC,
    else cc) as -std=c11 -Wall -Wextra -Werror -pedantic-errors; the
    DefaultWrapRules end-to-end test does the same for its datakit header
    (skip / no check without a C compiler).
  * Wrapper/ tests render hand-built plans (Wrapper/Fixtures/) and real
    planner plans of Facade/Model/samples/ into sandboxes with fake native
    files. Live (skip without the dotnet CLI; the second also without
    cargo): the generated projects are built with -warnaserror; sample 13
    is built in a sandbox, wrapped in pass-through mode, packed, referenced
    from a console app that prints add(2,3)=5, and its generated xUnit smoke
    test is run. Restores use the local NuGet cache (xunit.v3,
    CodeBrix.WebGpu.Native).
  * EndToEnd/ runs the public CrateBuilder from a crate (or a URL) to a
    wrapper that a generated console app builds with -warnaserror and runs:
    sample 15 (every datacrate shape), the rendering tests' every-shape crate
    planned by the real planner, sample 13 for linux-x64 + linux-arm64 with
    Packaging and `dotnet pack`, sample 17, sample 18 offline with a vendored
    path dependency, sample 16 WebGPU-linked (+ android-arm64 when an NDK is
    found), a WebGPU-linked facade that calls no engine function (the
    warning path), a cdylib that exports nothing (FacadeMode.Auto routes it
    through the facade stages; FacadeMode.Never keeps it and fails), a crate
    that does not compile, the readiness sets of
    both modes, the Git route (clone of the public repository, reuse,
    CleanUpAfterBuild; skips without github.com:443 - one probe shared by
    the whole run, Capabilities.CanReachGitHubAsync, three 20-second
    attempts, so parallel load cannot cause a false skip) and Docker mode (sample
    15 for linux-x64 + win-x64 in the rust image, ManyLinux linux-x64,
    ManyLinux linux-arm64 of sample 13 under qemu-user emulation with a
    wrapper (ELF AArch64, built natively in the arm64 container without
    --target, within glibc 2.28, `file` cross-check, the wrapper's
    runtimes/linux-arm64/native/*.so an AArch64 ELF; skips by name without
    /proc/sys/fs/binfmt_misc/qemu-aarch64; measured here at about 1.6 minutes
    from an engine without the arm64 image - pull, rustup under emulation,
    cargo and the wrapper build - so it is in the normal suite), ManyLinux
    linux-riscv64 of sample 13 (within glibc 2.39; skips by name without
    /proc/sys/fs/binfmt_misc/qemu-riscv64; measured here at about 2.5
    minutes; in the normal suite like the x64 and arm64 ManyLinux tests - the
    three run together or not at all), and
    sample 15 with the API extraction FORCED into the container
    (ApiExtractorDockerRoute.RunInContainer through the internal stages
    constructor), so that route runs on a machine with a host toolchain too,
    sample 16 WebGPU-linked for linux-x64 + win-x64 + android-arm64 (the
    Android file must carry API level 33, and a console app calls the linux
    build after the engine is loaded), and sample 13 for both Android ABIs
    without WebGPU (API level 24; the first run builds the Android image
    layer, a few minutes);
    isolated workbench labelled codebrix.rusttools.cratebuilder.tests.e2e
    with its own cache volume and container prefix, all removed at the end
    unless CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1), and a Docker-mode build of
    a workspace member whose root has [patch], .cargo/config.toml and
    [profile.release] settings (DockerWorkspaceEndToEndTests, its own
    workbench labelled codebrix.rusttools.cratebuilder.tests.workspace). The builds share the
    scratch root tests/.../bin/<configuration>/net10.0/cratebuilder-scratch/
    (the extractor is built once); each test's duration goes to e2e-log.txt
    in the test output folder.


PACKAGING AND PUBLISHING
========================
GeneratePackageOnBuild is true, so every build of a library project emits a
fresh .nupkg; one solution build produces all three packages.

Versioning is the CodeBrix date-stamped scheme, computed in the csproj from
System.DateTime.UtcNow as 1.<years-since-base>.<day-of-year>.<minute-of-day>.
It is monotonically increasing but is NOT SemVer. Two builds inside the same
UTC minute produce the SAME version - never publish two packages from one
minute. Do not replace the version block with a literal <Version>.

The three csproj files carry the canonical version block, each evaluated on
its own. CrateBuilder's dependencies on CodeBrix.RustTools and
CodeBrix.RustTools.Docker come from ProjectReferences, as the Docker
package's does; check its nuspec the same way before pushing. The Docker package's dependency on CodeBrix.RustTools comes from the
ProjectReference and always names the CodeBrix.RustTools version produced by
the same build; the two packages' own version numbers are normally the same
but can differ by a minute when a build straddles a minute boundary.
Publish both nupkgs from the same build, and check the dependency version
in the Docker nuspec before pushing, e.g.

    unzip -p src/CodeBrix.RustTools.Docker/bin/Release/<the .nupkg> \
        CodeBrix.RustTools.Docker.MitLicenseForever.nuspec | grep dependency

It must name CodeBrix.RustTools.MitLicenseForever with the version of the
CodeBrix.RustTools nupkg from that build, and
CodeBrix.Docker.MitLicenseForever with the pinned version.

What ships inside each nupkg (declared as <None ... Pack="true">):
    icon-codebrix-128.png    (PackageIcon; repo root, every package)
    README.md                (PackageReadmeFile; the root one for
                              CodeBrix.RustTools, the one beside the csproj
                              for CodeBrix.RustTools.Docker and
                              CodeBrix.RustTools.CrateBuilder)
    AGENT-README.txt         (the consumer guide; same split)
    THIRD-PARTY-NOTICES.txt  (repo root, every package)
MAINTAINER-README.txt, EXTRAS-README.txt, README-INDEX.txt, rust-samples/,
tools/ and the test fixtures are repo-only and are NOT packed.

The csproj files pin the CodeBrix.TomlParse, CodeBrix.Docker,
CodeBrix.LibGit2, CodeBrix.WebGpu.Native and CodeBrix.Templating versions
they were built against; bump a pin deliberately and re-run the full test suite.
Jeremy publishes the packages.


CODING CONVENTIONS
==================
  - Nullable reference types are OFF. Never use '?' on reference types
    (string?, MyClass?) and never use the null-forgiveness '!' operator.
    Value-type nullables (int?, DateOnly?, RustTool?) are fine.
  - No <ImplicitUsings> and no global usings: every file lists its own using
    directives at the top, System.* first, alphabetical.
  - File-scoped namespaces only (namespace X;), one type per file, no
    top-of-file banners.
  - XML doc comments on every public type and member, written to say
    something specific (not restatements of the identifier).
  - Public type names are unique across all namespaces (a consumer imports
    several namespaces at once); e.g. Building.CargoProfile (the enum) vs
    Projects.CargoProfileDeclaration (a [profile.*] table).
  - The library NEVER runs install/update commands. InstallGuidance is data.
    Every process the library starts goes through Internal/ProcessRunner so
    that cancellation kills the whole process tree.
  - CodeBrix.RustTools.Docker starts no processes of its own: every engine
    operation goes through CodeBrix.Docker. Everything it creates
    (containers, derived images, volumes) carries the <prefix>=1,
    <prefix>.workbench and <prefix>.kind labels from Internal/ResourceLabels
    so that cleanup is targeted, and the generated Dockerfile text must stay
    deterministic (its hash is the derived image's tag).
  - Every crates.io request must carry the descriptive User-Agent
    (RustWorkbenchOptions.EffectiveUserAgent) and go through the throttle in
    CratesIoClient.
  - Tests: files named <ClassUnderTest>Tests.cs (scenario files such as
    RustWorkbenchLicenseGateTests.cs are named for what they cover), method
    names in snake_case or MemberName_snake_case_description,
    //Arrange //Act //Assert comments in multi-statement tests,
    expression-bodied single-statement tests, SilverAssertions .Should()
    form, and TestContext.Current.CancellationToken passed to every
    cancellable call.


PROVENANCE AND VENDORED SOURCES
===============================
None. CodeBrix.RustTools was written from scratch for the CodeBrix family and
incorporates no third-party source code. THIRD-PARTY-NOTICES.txt describes the
external services the library calls and the captured outputs and public-data
excerpts used as test fixtures.

HARD RULE: nothing created by Apple (SDK files, headers, .tbd stubs, libSystem
binaries, Xcode content) may ever be added to this repository - no files, no
code, no fixtures, no excerpts - and the same goes for the third-party code
bases that are used, outside this repository, to validate the cross-build
toolchains: no files, no code, no fixtures, no excerpts, and no mention of
them by name anywhere in this repository.


NOTES
=====
  - Cargo's package version in the channel manifest is NOT the version
    `cargo --version` prints; the CLI version equals the rustc version of
    the release. RustReleaseInfo.CargoVersion therefore returns the rustc
    version and CargoPackageVersion keeps the manifest value.
  - Recent rustup releases honour RUSTUP_AUTO_INSTALL=0; every process the
    library starts gets it (unless the options set it explicitly) so that
    probing or building a project whose rust-toolchain.toml names an
    uninstalled toolchain never downloads it.
  - On macOS the C toolchain probe runs `xcode-select -p` BEFORE `cc`, because
    /usr/bin/cc is a stub that opens an installer dialog when the Command Line
    Tools are missing.
  - RustWorkbench's compiling commands run a readiness check first (cargo,
    rustc AND the C toolchain must not be Missing; Unknown only warns;
    FetchAsync / CargoCommand.Fetch needs only cargo and rustc and skips the
    metadata call, the license gate and the Linux cross check) and,
    with a LicensePolicy, run `cargo metadata` with the build's Offline /
    Locked / Frozen / feature flags before cargo. Detection is cached per
    workbench, keyed by the project folder plus the path and content of the
    toolchain file that applies to it (RustWorkbench.ToolchainCacheKey);
    tests replace the detector and the clock through the internal
    constructor and Clock property.
  - Streaming exec under Podman (2026-10-10): RunCargoAsync lost most of
    cargo's JSON lines over the Podman API socket because CodeBrix.Docker
    1.0.283.404 decoded a non-TTY exec as raw bytes whenever the daemon
    answered Content-Type application/vnd.docker.raw-stream - which Podman
    (and Docker before API 1.42) does for framed streams too - so stdcopy
    frame headers landed inside the lines. Fixed IN CodeBrix.Docker
    (ContainerExecStream follows the Tty flag), published as CodeBrix.Docker
    1.0.284.250, which RustTools.Docker now pins. RustTools.Docker needed no
    code change, only that package bump;
    DockerRustWorkbenchLiveTests.RunStreamingAsync_delivers_every_line_* fence
    the path (5000 lines in order, interleaved stderr, 64 KB lines). The live
    suite runs on the default engine (DOCKER_HOST, else
    /var/run/docker.sock); to try Podman, start `podman system service
    --time=0 unix://<socket>` and set DOCKER_HOST to it.
  - BuildContainerOptions.NetworkDisabled maps to ContainerSpec.NetworkMode
    "none" and is part of the reuse key (a network-less container is never
    handed out for a networked request). The intended flow is fetch in a
    networked container, build Offline in a network-less one on the same
    cargo cache volume (ContainerBuildLiveTests.NetworkDisabled_*).
  - Images: the generated layers are Debian/apt-based and are built with the
    docker CLI (CodeBrix.Docker's ImageOperations.BuildAsync runs `docker
    build`, BuildKit/buildx). A non-Debian base (manylinux + dnf) is the
    BuildImageSpec.Containerfile route: the workbench builds the consumer's
    own Containerfile through the same CLI build (DockerfilePath, BuildArgs,
    labels), tags it <ImageName>:containerfile-<hash> (ContainerfileImage:
    SHA-256 over the Containerfile bytes, the sorted build args and every
    context file, links by target and never followed) and uses it whole or
    as the FROM of the generated layers. With Podman and no docker CLI,
    DockerClientOptions.DockerCliPath = "podman" builds into the storage the
    API socket serves (verified 2026-10-10). CodeBrix.Docker has no
    daemon-side POST /build; adding one would remove the CLI requirement.
    The WebGpu repo's wgpu-native-tools/linux/Containerfile.x86_64 is the
    real-world consumer.
  - bindgen: AndroidLinkProfile and WindowsGnullvmLinkProfile add
    BINDGEN_EXTRA_CLANG_ARGS_<triple> (versioned Android target + NDK
    sysroot; mingw target + llvm-mingw root) - never the plain variable,
    which would reach host build scripts too. NDK 30 rejects unversioned
    Android triples in its headers. Linux cross needs nothing (clang finds
    Debian's /usr/<triple>); macOS has no headers to offer.
  - CargoCommandResult exit codes: -1 = killed (ProcessRunner, on cancel or
    timeout), -2 = LicenseGateFailedExitCode; never reuse either for
    anything else.
    The caller's CargoBuildOptions is cloned (CargoBuildOptions.Clone, which
    a test keeps in step with the property list), never mutated.
  - SourceArchiveExtractor picks the project with
    CargoProjectLocator.FindProjectRoots: the shallowest root wins, a
    workspace wins a tie.
  - rust-samples/test_project_10 must keep its warnings, and the vendor/
    crates of test_project_05 and the GPL member of test_project_04 must keep
    their license metadata: the tests depend on them.
  - CodeBrix.RustTools grants InternalsVisibleTo to CodeBrix.RustTools.Docker
    (src/CodeBrix.RustTools/InternalsVisibleTo.cs). The Docker package reuses
    CargoRunner.BuildArguments, CargoOutputCollector (and through it
    CargoJsonMessageParser), CargoProject.BuildMetadataArguments,
    RustWorkbench.ToMetadataOptions, CargoBuildOptions.Clone and
    CargoCommandResult.ForFailedLicenseGate / LicenseAudit, so a container
    build produces exactly the host result model. Changing those internals
    changes both packages; the Docker test project has no access to them
    and asserts the resulting command lines literally.
  - Container cargo runs as `setsid -w cargo ...` with
    CARGO_TARGET_DIR=/work/target and a per-run marker variable
    (CODEBRIX_RUSTTOOLS_RUN=<guid>). Cancellation and timeouts kill every
    process whose /proc/<pid>/environ carries the marker.
  - Windows targets link with llvm-mingw, never with Debian's GCC mingw-w64:
    the latter links msvcrt by default and has no aarch64 toolchain, and the
    maintainer wants UCRT only. rustc's gnullvm targets import
    libunwind.dll unless -C target-feature=+crt-static is set (a trailing
    -static link argument does NOT help: rustc's own -lunwind comes first),
    which is why WindowsGnullvmLinkProfile.RustFlags exists and the live
    test asserts that no "libunwind.dll" string is in the output. The layer
    picks the tarball by `uname -m` at image-build time, so the same
    Dockerfile text (same tag) yields an engine-specific image, like the
    Linux cross layers. llvm-mingw's licence files are kept in the image.
  - Android builds do NOT go through `cargo ndk`: cargo-ndk consumes
    cargo's --message-format=json output itself and prints none of it, so
    no diagnostics or artifacts would reach the parser. The library runs
    `cargo build --target <triple>` with the variables cargo-ndk would set
    (CC_/CXX_/AR_/RANLIB_<triple> and CARGO_TARGET_<TRIPLE>_LINKER/_AR
    pointing at the NDK's <triple>24-clang and llvm-ar, API level 24). The
    Android layer therefore does NOT install cargo-ndk; consumers who want
    it add it with BuildImageSpec.ExtraDockerfileLines.
  - The NDK's host tools are x86-64 Linux programs, so
    CrossTargetInfo.EnsureEngineCanBuild refuses Android targets on any
    engine whose DockerEngineInfo.Architecture is not x86_64/amd64
    (CrossTargetNotSupportedException). It runs in EnsureImageAsync and
    right after BuildAsync's engine check. Emulated amd64 images on ARM64
    engines are deliberately not set up.
  - Linux targets: CrossTarget.LinuxX64 / LinuxArm64 / LinuxRiscV64 are
    concrete and always mean that architecture; CrossTarget.LinuxNative (the
    default everywhere) is resolved to the engine's own one by
    CrossTargetInfo.Resolve before anything runs (pipeline, RunCargoAsync,
    RetrieveArtifactsAsync), so results and folders carry the resolved
    target. The engine's own Linux target builds natively (no --target, no
    layer); every other one gets a cross layer (DockerfileGenerator
    .LinuxCrossLayer: Debian's gcc-/g++-<arch>-linux-gnu + libc6-dev-<arch>-cross
    + rustup target) and the cross gcc as linker/CC/CXX/ar through
    CodeBrix.RustTools.LinuxLinkProfile (ContainerCargoRunner
    .LinuxCrossEnvironment). BuildImageSpec.WithoutNativeLinuxTargets strips
    the engine's own target from a spec before the image is built, so the
    same request gives a different (correct) image on each engine.
    ArtifactRetriever names folders after the target's own triple / RID and
    decides "native" from DockerEngineInfo.NativeLinuxTriple. The live tests
    use the fixture's NativeTarget, never a literal Linux target, so they
    pass on x64, ARM64 and RISC-V engines alike; the "Docker Linux cross
    image" collection (LinuxCrossFixture) builds sample 13 for the two
    foreign Linux architectures and checks the ELF machine type (x86-64
    0x3E, AArch64 0xB7, RISC-V 0xF3). Validated on the ARM64 engine
    (x86-64 + RISC-V cross builds); an x64 engine builds ARM64 + RISC-V the
    same way - Jeremy tests that.
  - The Android NDK release, URL and SHA-1 are constants on
    DockerfileGenerator and pinned by golden-text tests. Bumping the NDK
    changes the Android image hash (a full NDK download on every machine);
    take the SHA-1 from developer.android.com and update the tests with it.
  - macOS targets: the macOS layer is a Dockerfile heredoc (RUN <<'...'),
    so it needs BuildKit (every derived image build does already) and its
    whole script is part of the Dockerfile text and therefore of the image
    tag. It derives the libSystem stub from the image's own toolchain at
    image-build time, so a newer rust:latest re-derives automatically; the
    tag does not change with the toolchain (as for every layer). The
    runner's macOS environment comes from CodeBrix.RustTools.MacOSLinkProfile
    (ContainerCargoRunner.ContainerMacOSLinkProfile) so that the container
    and a Mac cannot drift apart; the install name comes from
    CargoBuildOptions.MacOSInstallName (BuildAsync defaults it from the
    project's single cdylib). CrossTarget.MacOSUniversal is a RETRIEVAL
    target: ContainerBuildPipeline.BuildTargetsFor expands it into the two
    slices, and ArtifactRetriever.RetrieveUniversalAsync joins them with
    MacOSUniversalBinary; CreateInvocation and RunCargoAsync reject it.
    Keep CrossTargetInfo.Table in CrossTarget enum order (a test checks).
  - Build containers have no init process: the main process is
    `sleep infinity`, which does not reap orphans, so killed processes stay
    as zombies until the container is removed. Harmless; do not "fix" it by
    adding an init without updating the cancellation test and the docs.
  - UNTRUSTED CODE: third-party Rust code the maintainer does not trust is
    built only in a dedicated container with its own volumes that is
    destroyed afterwards, never on the host and never in this repository's
    test containers. Build scripts and procedural macros execute at compile
    time. The scratch harness mentioned in EXTRAS-README.txt follows this
    rule and lives outside the repository.
  - Android on the host (CodeBrix.RustTools): Toolchain/AndroidNdk +
    AndroidNdkLocator find the NDK (ANDROID_NDK_HOME, ANDROID_NDK_ROOT, then
    the newest ndk/<version> under ANDROID_HOME / ANDROID_SDK_ROOT, then
    ndk-bundle; RustWorkbenchOptions.AndroidNdkPath overrides, with no
    fallback) and record it in RustToolchainInfo.AndroidNdk during
    detection. Host tags follow Google's NDK host support: linux-x86_64 and
    windows-x86_64 need an x86-64 machine, darwin-x86_64 serves Intel and
    Apple Silicon Macs (universal binaries). Building/AndroidLinkProfile is
    the single source of the CC_/CXX_/AR_/RANLIB_ and
    CARGO_TARGET_<T>_LINKER/_AR variables: ContainerCargoRunner.AndroidEnvironment
    now comes from it (ContainerAndroidLinkProfile, its strings are pinned by
    ContainerCargoRunnerTests), and RustWorkbench.BuildAndroidAsync applies it
    per target. Validated on the Apple Silicon Mac with NDK r30 (both ABIs,
    jniLibs and NuGet layouts); the Windows (.cmd wrappers, .exe tools) and
    Linux x86-64 paths follow the documented NDK layout but have NOT been
    run yet - Jeremy tests those machines.
  - The AI-agent pointer stubs at the repo root (AGENTS.md, CLAUDE.md,
    .clinerules, .cursorrules, .cursor/rules/agent-readme.mdc, .windsurfrules,
    .github/copilot-instructions.md, .junie/guidelines.md) all point at
    README-INDEX.txt. Keep them in sync with the canonical family versions;
    they are not per-repo content.

CRATEBUILDER
============
Pipeline: CrateBuilder.BuildAsync validates and copies the request, then
Internal/Pipeline/CrateBuildPipeline.RunAsync runs the stages in
CrateBuildStage order. Each stage sits behind an internal interface; the
implementations are chosen in CrateBuilderStages.CreateDefault(), and tests
pass fakes through the internal CrateBuilder(CrateBuilderOptions,
CrateBuilderStages) constructor. The seams (all in Internal/Pipeline/):
    IReadinessChecker   Readiness            Internal/Readiness/ReadinessChecker
    ISourceResolver     ResolvingSource      Sources/SourceResolver
    IWorkspaceStager    Staging              Staging/WorkspaceStager
    IFacadeGenerator    ExtractingApi, Planning, RenderingFacade
                                             Facade/FacadeGenerator
    IBuildDriver        Building             Building/ModeBuildDriver (Host- or
                                             DockerBuildDriver with the request's
                                             contributions and verifiers)
    IArtifactVerifier   Verifying            Building/Verification/VerificationRecorder
    IWrapperWriter      Wrapping             Wrapper/WrapperProjectWriter
  Data between stages: ResolvedSource, StagedWorkspace, FacadeGenerationResult
  (+ FacadeExport); CrateBuilderContext carries the options, the run folder
  (<scratch>/runs/<utc stamp>-<8 hex>), the lazily created RustWorkbench and
  DockerRustWorkbench (owned by the CrateBuilder), the progress sink, the
  readiness report, warnings, and the ToolchainDescription /
  WebGpuNativeVersion a stage may set for the record.
Rules the pipeline enforces (fenced by CrateBuilderPipelineTests):
  * ArgumentException from Validate propagates; every stage failure becomes
    FailedStage + FailureMessage. A CrateBuilderException's own Stage wins
    (FacadeGenerationException names ExtractingApi / Planning /
    RenderingFacade); one created without a stage gets the running stage.
  * Readiness failure: no record, no run folder. After Readiness the record
    (crate-build-record.json in OutputDirectory) is always written, then
    CleaningUp runs (ResolvedSource.CleanUp, then the run folder unless
    KeepScratch) - also after the caller's cancellation, which then
    propagates. Request.Timeout covers the stages after Readiness and is
    reported as a failure of the running stage.
  * Unbuildable targets become NotBuildableInThisMode (readiness guidance
    attached); the build driver receives a request copy holding only the
    buildable targets. No compiled target = a Building failure.
  * The facade stages run per request.FacadeMode (CrateBuildPipeline
    .NeedsFacade): Always / Never as named; Auto when a wrapper is requested
    or the staged crate's CrateTypes lack "cdylib" (a cdylib without a
    wrapper is built as it is first, without extracting its API, so a plain
    cdylib build never needs the extractor or its crates). When that build
    leaves a target Failed with DefaultArtifactVerifier.NoExportsNote in its
    message (NeedsAutoFallback), the same run generates the facade and calls
    the build driver again (warning + FacadeRecord.Reason =
    AutoFallbackReason). A generated facade replaces
    StagedWorkspace.ManifestPath and sets IsFacade; StagedWorkspace.CrateName
    becomes the plan's LibName (the facade's [lib] name = the native file's
    base = the wrapper's DllImport name), never the facade's PACKAGE name
    (FacadeGenerationResult.CrateName, what -p takes).
  * The wrap report is written into the run folder (report/WRAP-REPORT.txt +
    .json) and copied next to the build record in Recording
    (CrateBuildResult.WrapReportPath, BuildRecord.WrapReportFile); the record
    gets a FacadeRecord (Generated, LibName, PackageName, the exports).
  * Default provenance: macOS targets are Distributable only in host mode on
    a Mac, else DevelopmentBuild.
Readiness: ReadinessReportBuilder.ForHost / ForDocker assemble the report
from detection data (testable without tools); HostTargetEvaluator uses
ToolchainRequirements / ToolchainReadiness / InstallGuidanceProvider,
MacOSLinkProfile.ForToolchain and WindowsGnullvmLinkProfile.ClangPath (the
llvm-mingw search: CrateBuilderOptions.LlvmMingwDirectory, LLVM_MINGW_HOME,
PATH); DockerTargetEvaluator uses CrossTargetInfo (Android needs an x86-64
engine; a derived image needs the docker CLI).
Targets: CrateTargets maps every CrateTarget through CrossTargetInfo of the
Docker package (ToCrossTarget), so both modes use one triple per RID.
Sources and staging (Sources/, Staging/): the reuse rule, the clean-up rule
and the staging layout are written out in the XML remarks of SourceResolver
and WorkspaceStager. In short: a clone folder that already holds a non-bare
clone of the same origin URL is fetched and checked out again (safe
checkouts, detached at the remote default branch's tip when nothing is
named); anything else in a non-empty folder is an error and is never
touched; CleanUpAfterBuild deletes the clone folder (also the consumer's
own). Staging copies the workspace root (WorkspaceRootFinder = cargo's
nearest-[workspace] rule, which lives in CodeBrix.RustTools'
CargoProjectLocator.FindWorkspaceRootManifest - strict mode, with the clone
root as boundary - so the two packages share one rule) to
<run>/stage/<package>/src/, vendors path dependencies that leave the copied
tree to .codebrix-vendor/<n>-<name>/ (a SIBLING of src/, so cargo never sees
them as implicit members) and rewrites only the staged manifests losslessly
(TomlParse syntax tree). Never copied: .git, cargo's target/, .codebrix-*
folders at any depth, the VCS / CI / editor root folders listed in
StagingFileSystem.ExcludedRootHiddenFolders (other hidden root folders such as
.cargo and .sqlx ARE copied: builds read them), and the scratch root / run folder
when they lie inside the crate (StagingFileSystem.CopyTree's excluded
folders; without that a scratch folder inside the crate copied itself until
the path was too long). SourceHash = SHA-256 over (path, NUL, bytes, NUL)
of every staged file in ordinal path order.
API extraction (Facade/Extraction/, Facade/Model/): codebrix-api-extractor is a
small syn-based Rust program whose SOURCE lives in Facade/Extraction/Tool/
(Cargo.toml, Cargo.lock, src/*.rs, SCHEMA.md) and is embedded in the assembly
(EmbeddedResource, logical names ApiExtractorTool/<path>). ApiExtractor
materialises it to <scratch>/api-extractor/<16 hex of its SHA-256>/, builds it
once there (host: RustWorkbench, release, --locked; Docker without a usable host
toolchain: in a container - on the in-container route the tool and the staged
crate are copied side by side into /work/codebrix-api-extractor and /work/crate
with CopySourceIntoAsync) and runs it against the staged crate; the JSON
(schema version 1, SCHEMA.md) is read into CrateApiModel. Path types carry the
tool's own name resolution (resolve.rs annotate_types: "resolved" = the canonical
path of the crate's struct / enum / alias the path names in the module where it
was written, through its use declarations and globs; "external" = the first
segment is imported from another crate; "unresolved" = nothing of that name is in
scope there), and PlannerRun.ResolveLocal trusts them before its older
module / crate-root / unique-name lookups (kept for the 2015 edition and for
documents without the fields). SCHEMA.md and the C#
model must change together: CrateApiModelTests round-trips the checked-in
documents in tests/.../Facade/Model/samples/ and fails on any property the model
does not mirror. Changing the tool: edit the files in Tool/, build and clippy a
COPY of the folder in scratch (never run cargo inside src/ - it writes target/),
copy a regenerated Cargo.lock back if dependencies changed, and regenerate the
three sample documents from copies of rust-samples/test_project_15, _17 and the
edge-case workspace (run the tool from the folder holding them, so the paths in
the documents stay relative). A changed source gives a new folder hash, so every
CrateBuilder version builds its own copy.
Planning (Facade/Planning/): MappingPlanner turns the CrateApiModel and an
optional WrapPlan into a MappingPlan (Facade/Planning/Model/, plain data the
renderers walk as is). PlannerRun applies the tier-1 rule table in this order:
reachability and cfg (CfgEvaluator, per requested target, three-valued), the
WrapPlan selection (PathGlob: '*' = any run, tried on every path form with and
without crate::), C-like enums, mirrors (Copy, all fields public, C-able
fields, recursive), handles (any other non-generic struct), functions (type
table in TypeMappings), field accessors of handles (AddFieldAccessors: a
getter per pub field that crosses C, a setter, a from-fields constructor
when all fields are pub and cross C; then DropUnusedHandles keeps a handle
only when a crate function uses it, or it has a from-fields constructor, or
Default plus accessors, and keeps what the kept accessors hand out), names,
overrides, release functions, constants (ConstantLiterals: plain literals of
number / bool / &str type become C# const values; nothing crosses C), doc
notes. Result and argument rewrites live in the planner and reach the
renderer only as RustCallExpression text: OwnedReturn wraps the call ("{0}")
to turn a shared-reference result into an owned copy (Clone::clone,
String::from, to_vec, .cloned()) or a path / OS string into a Result<String,
&str> (to_str, never lossy); argument expressions build PathBuf / OsString /
Path::new from the &str local; text-bounded generics are substitutions of
&str / &[u8] (rustc infers them from the argument). Lists are
MappingKind.ValueSlice (in) / ValueVec (out) with the element mapping in
TypeMapping.Element: plain values convert element by element (mirror / enum /
bool / array conversions); object elements are pointers (in: handle_ref +
clone per element, the wrapper AddRefs every object for the call; out:
HandleBox::boxed per element); string elements are <prefix>str_ref structs in
(the wrapper packs all UTF-8 bytes into one pinned array) and a buffer of
Buffer structs out (Buffer::from_buffers). Data-carrying enums are handles
keyed by their RustEnum in PlannerRun.handles; AddVariantName adds Variant()
and one constructor per variant (Generated "variant", like field accessors
they never keep an otherwise unused handle). Names: ONE C namespace - the fixed names
(<prefix>buffer, last_error_message, free_buffer, free_string, status), the
<PREFIX>STATUS_* macros, the fixed-array structs, then every type (ordinal
by Rust path), every enum constant and every function are de-duplicated
together ("_2"), because the renderer refuses any C-name clash and a C
header cannot hold a typedef and a function of one name. Every list in the
plan is ordered ordinally (or by mirror dependency), so planning twice gives
identical JSON (MappingPlannerTests fences it on the three model samples, and
a live test fences that a fresh extraction of samples 15 and 17 plans exactly
like the checked-in samples). WrapReportWriter writes WRAP-REPORT.txt and its
JSON twin WRAP-REPORT.json (MappingPlanJson: back references written as C
names).
Rendering (Facade/Rendering/): FacadeRenderer turns a MappingPlan into the facade
crate under <stage>/.codebrix-facade/<lib>_facade/ (Cargo.toml, Cargo.lock copy,
src/lib.rs, <lib>.h, build.rs when WebGPU-linked, FACADE-README.txt,
generated-files.json). FacadeSourceBuilder holds every type decision (FFI types,
conversions, checks, export bodies); the Scriban templates in
Facade/Rendering/Templates/ (embedded resources) only lay text out, under the
rules in FacadeTemplates (fresh context, strict variables, limits that throw,
"\n"). GeneratedFilePublisher renders into a temporary folder, publishes
atomically, rewrites only changed files and removes only files the previous
generated-files.json listed. FacadeConventions holds the status codes the C
header, the wrapper and the docs quote. The facade is its own workspace, and
cargo reads [patch] / [replace] and [profile.*] only from the root manifest of
the workspace being built, so FacadeRenderer.WorkspacePatches and
.WorkspaceProfiles copy those tables of the staged workspace root into the
facade's Cargo.toml (paths re-based; panic forced to "unwind" in dev and
release).
Template pitfall (MSBuild culture detection): an EmbeddedResource named like
"X.cs.sbn" or "X.de.sbn" is taken as a satellite resource of that culture (cs =
Czech) and moved out of the main assembly unless the item sets
WithCulture="false". Both template ItemGroups in the CrateBuilder csproj set it;
any new embedded-resource pattern needs it too.
Building (Building/): ModeBuildDriver picks HostBuildDriver or
DockerBuildDriver per request and hands it the contributions (WebGpuLinkStager
when LinkToCodeBrixWebGpu) and the verifiers (DefaultArtifactVerifier, plus
WebGpuLinkVerifier when linked; the Docker driver adds GlibcFloorVerifier for
ManyLinux). The drivers verify every artifact ONCE, right after it is built;
the Verifying stage (VerificationRecorder) only records - it copies notes that
start with "WARNING: " into the build's warnings. A facade build gets no
--locked and no --features (CargoOptionsFactory, isFacade: the facade's lock
is the upstream's copy without the facade's own entry, and the features are
in the facade's dependency line). The Docker driver copies the WHOLE staging
root (DockerBuildDriver.CopyRootFor) to /work and runs cargo per target in
the built manifest's folder, given as an ABSOLUTE container path
(ContainerWorkingDirectoryFor). All targets share ONE container: each target's
cargo run gets its own options (its contribution environment; contribution
rustflags in CARGO_TARGET_<TRIPLE>_RUSTFLAGS, which CodeBrix.RustTools.Docker
appends to its own per-target flags), and the container links Android at
HostBuildDriver.AndroidApiLevelFor (BuildContainerOptions.AndroidApiLevel, 33
when WebGPU-linked) - the same level as host mode. The "{workspace}" contribution token is the staging root on
the host and /work in Docker mode. The ManyLinux limit (Linux targets only) is
TargetMatrix.ManyLinuxLimit, used by the readiness check and the driver alike.
ManyLinux builds one image and one container per platform
(DockerBuildDriver.GroupsFor; the rust image keeps all targets in one): the
x86_64 / aarch64 / riscv64 Containerfiles (ManyLinux/, embedded) are written to
<run>/manylinux-<arch>/; CreateSpec sets BuildImageSpec.Platform for aarch64 and
riscv64 (linux/arm64, linux/riscv64) and leaves x86_64 without one, so the x64
image keeps the identity it always had (on a non-x86-64 engine the driver sets
linux/amd64 itself). Before building, the driver probes every platform that is
not the engine's own (DockerRustWorkbench.ProbePlatformAsync, cached per
workbench); a failed probe makes the target NotBuildableInThisMode with
ManyLinuxImages.EmulationUnavailableReason (qemu-user binfmt, the probe's
message, the EnableForeignPlatforms guidance). The readiness check only warns
(docker.manylinux-emulated). GlibcFloorVerifier takes the per-target floor
(ManyLinuxImages.GlibcFloor: 2.28, 2.28, 2.39). With a shared cargo target
folder each ManyLinux image gets docker/manylinux-<arch>/ (SharedCargoTarget
.ManyLinuxFolderFor): cargo's triple folders do not separate them, because each
image builds its own architecture natively into release/ (verified: the
manylinux containers run cargo without --target). The riscv64 base is
manylinux_2_39 because quay.io/pypa publishes no older riscv64 baseline
(manylinux_2_28/2_31/2_35_riscv64 do not exist); the three digests (dated tag
2026.07.19-1) are the ones the other CodeBrix native-library builds pin, and
the rustup-init SHA-256s are the ones static.rust-lang.org publishes for the
pinned rustup release (rustup-init.sha256 beside each host's rustup-init).
WebGPU verification: an artifact that links libcodebrix_wgpu by name passes;
one that imports wgpu* symbols (ELF / Mach-O undefined symbols, read by
WebGpu/Internal) or exports them, or links another wgpu library, fails; one
that references the engine not at all (the linker drops an unused library,
e.g. a WebGPU-linked facade with no engine call) passes with a WARNING note.
WebGPU staging (WebGpu/): NativePackageLocator finds the CodeBrix.WebGpu.Native
package folder (CrateBuilderOptions.WebGpuNativePackageDirectory, else the NuGet
global packages folder at the version the csproj pins, then the application's
runtimes/ for the binaries only); WebGpuLinkStager (an IBuildContribution)
copies include/ and the target's engine binary into <stage>/.codebrix-wgpu/ and
returns CODEBRIX_WGPU_INCLUDE_DIR / CODEBRIX_WGPU_LIB_DIR and "-L native=..."
with the "{workspace}" token, so one contribution serves both modes;
WebGpuLinkRecipe holds the crate-side lines the generated facade gets.
Verification notes (DefaultArtifactVerifier): format and architecture, the
needed libraries, the export count (first ten names), the highest GLIBC_
version (ELF), and for Android the API level of the NDK's .note.android.ident
(ElfFileReader; "linked for Android API level N"), which is how the tests
prove that both drivers link Android at 24, or 33 when WebGPU-linked.
Wrapping (Wrapper/): WrapperProjectWriter (the IWrapperWriter) resolves the
folder, refuses a non-empty folder without generated-files.json, builds the
template model (Generation/WrapperModelBuilder: names, the <Lib> / <Lib>Library
entry class rule, member placement, the first README example = the first entry
that is not a constant, documented signatures without the global::<RootNamespace>.
qualification the code needs; FunctionEmitter writes every member body;
ManagedTypes spells the C# types and qualifies a type name only inside a class
where a member has that spelling) and renders Templates/*.sbn
(Rendering/WrapperTemplates; same strict Scriban rules as the facade). It then
copies runtimes/<rid>/native/, the crate's licence files (licenses/), the wrap
report, and writes generated-files.json (GeneratedFilesManifest: only listed
files are replaced or removed on regeneration). Generated objects are sealed
partial classes over an internal SafeHandle (Handle.cs.sbn).
Fixtures: rust-samples/test_project_15 (datacrate: every tier-1 shape), 16
(wgpu_link_probe: WebGPU route 1), 17 (hardcrate: mostly unmappable) and 18
(a staging workspace) are the checked-in inputs (EXTRAS-README.txt). Several
tests write small neutral crates into their sandbox instead: CoverageCrate
(Facade/Rendering/Fixtures: every C shape), shapekit and its workspace
(CrateShapesEndToEndTests: lifetimes, features, non_exhaustive, imports,
nested workspaces, [patch] and .cargo/config.toml), datakit
(DefaultWrapRulesEndToEndTests: fields, copies, paths, lists, constants, data
enums), wskit (DockerWorkspaceEndToEndTests) and gitfix (Sources/
GitTestRepository: local upstream repositories with tags, branches and a
submodule, made with CodeBrix.LibGit2). New fixtures use neutral names only.
Docker test fixtures: each has its OWN workbench (label prefix, cargo cache
volume, container name prefix) and removes its containers, volumes and
derived images at the end unless CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1:
    DockerDriverFixture (Building/)        codebrix.rusttools.cratebuilder.tests
    ApiExtractorDockerTests (Facade/...)   codebrix.rusttools.cratebuilder.tests.extract
    DockerEndToEndFixture (EndToEnd/)      codebrix.rusttools.cratebuilder.tests.e2e
    DockerWorkspaceEndToEndFixture         codebrix.rusttools.cratebuilder.tests.workspace
The derived images are rebuilt from the engine's BuildKit cache on the next
run (the Android and Windows layers take minutes only the first time on a
machine). After a run, `docker ps -a` / `docker volume ls` /
`docker images` filtered on label=<prefix>... must be empty.
End-to-end suites: EndToEnd/ (Host, Git, Docker, CrateShapes, DefaultWrapRules,
DockerWorkspace) is the acceptance suite - see TESTING for what each builds.
Every change to the planner, renderer, wrapper or drivers must leave them
green, and a fix gets a fence there or in the unit tests (neutral names).
Packing: `dotnet pack src/CodeBrix.RustTools.CrateBuilder -c Release -o <dir>`
(any Release build also packs, GeneratePackageOnBuild). The nupkg holds
lib/net10.0/CodeBrix.RustTools.CrateBuilder.dll + .xml, README.md,
AGENT-README.txt, THIRD-PARTY-NOTICES.txt and icon-codebrix-128.png at its
root; inside the assembly are the embedded resources ApiExtractorTool/*
(Cargo.toml, Cargo.lock, SCHEMA.md, src/*.rs - never a target/ folder),
CodeBrix.RustTools.CrateBuilder.Building.ManyLinux.Containerfile.x86_64,
CodeBrix.RustTools.CrateBuilder.Facade.Rendering.Templates.*.sbn and
CodeBrix.RustTools.CrateBuilder.Wrapper.Templates.*.sbn. Check the list with
`unzip -l` on the nupkg and System.Reflection's
GetManifestResourceNames() on the packed dll before publishing.
Sweeps (shake-out runs over many real crates): never from inside this
repository and never against code you are not allowed to build. Write a
scratch console driver OUTSIDE the repository with a ProjectReference to
src/CodeBrix.RustTools.CrateBuilder/CodeBrix.RustTools.CrateBuilder.csproj.
Give it a list file (one line per crate: a local Cargo.toml path or a Git URL
+ manifest path, and a neutral wrapper name), copy local sources into the
scratch folder first (rsync without .git / target), set CARGO_TARGET_DIR to a
scratch folder per source repository (shared dependencies are then built
once), and per crate call CrateBuilder.BuildAsync with LocalManifestSource or
GitRepositorySource, Mode Host or Docker, the targets, an OutputDirectory under
scratch, Wrapper = { AssemblyName = RootNamespace = "<neutral name>" } and a
per-crate CancellationToken (20 minutes is ample). Append one JSON line per
crate to a results file (Success, FailedStage, FailureMessage, every
CrateTargetResult, Record.Facade.ExportCount, the wrap report's counts and
Elapsed) so a run can resume where it stopped, then `dotnet build
-warnaserror` each generated wrapper. Classify every failure by rebuilding
the staged copy with plain cargo (KeepScratch): upstream (plain cargo fails
the same), environment (a missing system library), or a CrateBuilder bug.
A bug is fixed here with a fence that uses a neutral synthetic crate - never
a copy of the swept code - and nothing from the swept crates (names, paths,
output) is written into this repository. Docker sweeps use a workbench with
their own label prefix and call CleanupAsync at the end. Delete the scratch
target folders afterwards (they grow to tens of gigabytes).

PHASE 3: macOS CROSS-BUILD FROM LINUX (IMPLEMENTED 2026-10-09)
==============================================================
Status: IMPLEMENTED and validated on an Apple Silicon Mac (macOS 27,
Xcode 27 command-line tools, Rust 1.99.0 on both sides, Docker Desktop 4.94
with an ARM64 engine). What the packages now contain:

  CodeBrix.RustTools.Docker  DockerfileGenerator.MacOSLayer (both Darwin
                             targets + llvm-tools from rustup, then a heredoc
                             script that derives the libSystem stub into
                             /opt/codebrix/macos-sysroot and links the
                             toolchain's rust-lld to /opt/codebrix/bin/rust-lld);
                             CrossTarget.MacOSArm64 / MacOSX64 supported on
                             any engine architecture; CrossTarget.MacOSUniversal
                             (a retrieval target: both slices are built and
                             joined with MacOSUniversalBinary; NuGet RID osx);
                             ContainerCargoRunner.MacOSEnvironment;
                             DockerRustWorkbench.ExportMacOSSysrootAsync and
                             RetrieveMacOSUniversalArtifactsAsync; the "Docker
                             macOS image" test collection.
  CodeBrix.RustTools         RustToolchainInfo.Sysroot (rustc --print sysroot);
                             CargoBuildOptions.MacOSInstallName;
                             Building/MacOSLinkProfile (the shared link
                             settings); MacOSUniversalBinary (+ MacOSBinarySlice,
                             MacOSUniversalFile, MacOSUniversalBuildResult);
                             RustWorkbench.BuildMacOSUniversalAsync.

The rule (unchanged)
--------------------
We redistribute NOTHING created by Apple. No Apple SDK files, no headers, no
.tbd stubs from Xcode, no libSystem binaries, no copies of anything from an
Xcode installation - not in this repository and not required in a container
at run time. Nothing of Apple's was used to build the feature either; Apple's
tools on the Mac were used only on our own output, to validate it (launch,
otool, codesign, nm). The
repository's README.md and THIRD-PARTY-NOTICES.txt, both READMEs of
CodeBrix.RustTools.Docker and CrossTargetInfo.MacOSDevelopmentOnlyNotice (the
notes of every macOS target at run time) state (wording of 2026-10-09):

    IMPORTANT NOTICE: macOS binaries produced by CodeBrix.RustTools.Docker
    are built in Docker containers without Apple's SDK, tools or files and
    are for development and testing only. They must not be distributed or
    used in any production environment. Any macOS build that will be
    distributed or run in production must be produced on a Mac and can be
    created using CodeBrix.RustTools, where Apple's toolchain, SDK and
    code signing are available under Apple's own license terms.

Findings (what only the spike could tell us)
--------------------------------------------
* A binary linked in the container LAUNCHES on the Mac. rust-lld's ld64
  flavour writes the ad-hoc signature itself (codesign -dv: Signature=adhoc,
  flags linker-signed); the kernel accepts it; rcodesign is not needed and is
  not used.
* The set-difference derivation over the rlibs (241 names for arm64 before
  filtering) picks up two groups that are NOT libSystem exports: the
  allocator shim rustc generates at link time (v0-mangled names in the
  __rustc pseudo-crate) and the section$start$ / section$end$ boundary
  symbols of the profiler runtime (linker-synthesized). They are filtered
  out. Three names appear ONLY at link time and are added by closing the
  loop (link a hello-world against the stub, absorb lld's "undefined symbol"
  lines, regenerate, repeat - two rounds): dyld_stub_binder (the linker's
  lazy-binding helper), __tlv_bootstrap and _memmove. Counts with Rust
  1.99.0 from the rlibs and a hello-world alone: 225 symbols for
  aarch64-apple-darwin, 222 for x86_64-apple-darwin (210 common; the x86_64
  list has the $INODE64 / $NOCANCEL variants). Every derived name is a
  documented libSystem export (C library, pthread, dyld or math), and the
  linked binaries launch on the Mac, which is the test that counts.
* The rlibs alone are NOT enough (found 2026-10-09 by building larger
  third-party pure-Rust crates, outside this repository, with the profile on
  both sides): f64::powf, ln, exp, sin, % on floats and friends are inlined
  into the crate being compiled and lowered by LLVM to C library calls
  THERE (_pow, _log, _exp, _fmod, and on Darwin ___sincos_stret, ___exp10,
  _memset_pattern16), so no std rlib ever imports them. A stub derived from
  the rlibs links a hello-world but fails any crate that does float math:
  "rust-lld: error: undefined symbol: _pow". The derivation now also
  compiles a probe program (DockerfileGenerator.MacOSProbeProgram: every
  float and memory operation that lowers to a library call, for f64 and
  f32) and harvests its undefined symbols, and then adds
  DockerfileGenerator.MacOSStubSupplement: the ISO C99 <math.h> function
  names (double and float), the POSIX lgamma_r / lgammaf_r variants Rust's
  std declares, and the Darwin library calls LLVM can emit. The supplement
  covers what the probe does not happen to need on one target (floor, ceil,
  trunc, round, fma are instructions on arm64 but calls on x86_64) and
  functions reached through extern "C" declarations. Names come from the C
  standard and from LLVM's TargetLibraryInfo, confirmed against public
  documentation and by linking (the float variant of lgamma_r is lgammaf_r,
  as Rust's std declares it; there is no lgamma_rf). Counts with Rust
  1.99.0 are now 344 (aarch64) / 341 (x86_64), 332 common. The probe is
  also what the closed loop links. The unit tests
  MacOSStubSupplement_is_a_sorted_unique_list_of_c_symbol_names and
  Generate_embeds_the_probe_program_and_every_supplement_symbol_in_the_macos_layer
  pin the list and the layer, and the live test
  BuildAsync_links_a_cdylib_that_uses_float_math_for_both_macos_targets
  links a cdylib that uses pow, ln, sin_cos and % for both targets. A stub
  exported from an image built before this change lacks the names; export
  it again (the image tag changed with the Dockerfile text).
* rustc links Darwin targets with "-lSystem -lc -lm": the sysroot needs
  libc.tbd and libm.tbd too. On macOS the C library, the math library,
  pthreads and dlopen are all parts of the single libSystem umbrella (there
  is no separate libc or libm to link; the public man pages say so), so the
  generator writes libc, libm, libpthread and libdl as copies of our stub,
  all naming /usr/lib/libSystem.B.dylib; otool -L on our binaries shows that
  one library and nothing else.
* SDKROOT=<sysroot> is the switch, on BOTH sides: rustc then passes
  -syslibroot itself and "-platform_version macos 11.0.0 11.0.0" (deployment
  target = SDK version), and never runs xcrun. Without it, on the Mac rustc
  finds Xcode's SDK by itself - and the Mac's own rust-lld (LLVM 23) rejects
  the SDK 27 .tbd files as "malformed file: unknown target". Our stub is
  therefore REQUIRED on the Mac for rust-lld, not merely nice for identity.
* The gcc-ld/ld64.lld wrapper cannot be cargo's linker together with
  -C linker-flavor=ld64.lld (rustc and the wrapper both add a flavour
  argument: "unknown argument '-flavor'"); the bare rust-lld works, rustc
  adds "-flavor darwin" itself.
* -C link-arg=-S keeps the STABS debug map (object paths and mtimes) out of
  the output. cargo's default release strip (debuginfo) produces identical
  output on both sides with Rust 1.99.0, so the profile leaves the user's
  profile alone.
* cargo's -C metadata hash is the same across hosts and build paths ONLY
  for crates whose dependency tree contains no build script and no
  proc-macro (sample 13, and the plugin fixtures of the 2026-10-09
  validation, which depend on pure binding crates only). cargo hashes
  rustc's verbose version into every unit's metadata and skips the
  "host: <triple>" line only for TARGET units (hash_rustc_version in
  cargo's compilation_files.rs: "if unit.kind.is_host() ||
  !line.starts_with("host: ")"), so a build script or proc-macro, which is
  compiled for the host, gets a different -C metadata on macOS than on
  Linux, and every crate that depends on one (serde, thiserror, num-traits,
  anything with a derive) inherits the difference through its dependencies'
  metadata hashes. Measured with cargo 1.99.0: cfg_if, clap_sys, libloading,
  log and strength_reduce had identical -C metadata on the Mac and in the
  container; serde, serde_core, thiserror, num-traits and everything above
  them did not. Consequence: for such a crate the Mac and the container
  compile the same CODE but mangle the symbols differently, so the linked
  files are not byte-identical and the rlibs differ in their object-file
  names. Byte-for-byte identity is therefore promised only for dependency
  trees without host-compiled units; nothing in this repository can change
  that, it is cargo's hashing. (-C extra-filename additionally hashes the
  rustflags unless they contain --remap-path-prefix, which ours do, so the
  dependency file names still agree.)
* rlibs are not identity candidates even without host units: lib.rmeta
  records the absolute path of the workspace root and of every source file
  (/work/... in the container, the checkout path on the Mac), so the rlib
  of a workspace member differs while its object files, and a cdylib or
  program linked from them, do not. Four of five leaf registry crates
  compared as byte-identical rlibs across the two hosts once the registry
  path is remapped; the lib.rmeta of the fifth (log) differed for a reason
  that was not investigated.
* Every remaining byte difference was path- or name-derived: the ad-hoc
  signature's identifier is the OUTPUT FILE NAME (the cargo target name is
  the same on both sides, so it matches); a cdylib's LC_ID_DYLIB defaults to
  the output path (-install_name, i.e. CargoBuildOptions.MacOSInstallName,
  defaulted from the package's single cdylib); the cargo registry source
  folder differs (--remap-path-prefix to /cargo-registry-src). A sample that
  embeds env!("CARGO_MANIFEST_DIR") (02) or the build time (06) can never be
  identical across paths or runs; 13 is the identity population.
* RESULT: sample 13 (release) built in the container and on the Mac with the
  same profile - test_project_13 and libtest_project_13.dylib - is
  byte-for-byte identical (SHA-256 equal), ad-hoc signature included; the
  live test Container_and_host_builds_of_sample_13_are_byte_for_byte_identical_on_a_mac
  repeats it through the public APIs on every run on a Mac. Identity is a
  property of this setup (same Rust release and LLVM, same stub, same
  flags); it is not promised across releases or setups.
* The engine's architecture is irrelevant for macOS: rustup ships the Darwin
  std for every host and rust-lld links either slice. The ARM64 engine built
  the x86_64 slice (validated: thin Mach-O, cputype x86_64) and would run on
  an Intel Mac; an x86-64 engine builds arm64 the same way.
* Universal files: MacOSUniversalBinary writes the fat header and lays the
  slices out like lipo (by cputype, x86_64 first, 4 KiB / 16 KiB alignment);
  the slices are copied unchanged so their ad-hoc signatures stay valid, and
  the container-made universal program launches on the Mac. Identity with a
  file made by Apple's lipo was NOT checked and is not a goal.
* macOS has no coreutils `timeout`; validate-on-mac.sh falls back to a perl
  alarm.

The recipe (what MacOSLinkProfile applies, per Darwin triple)
-------------------------------------------------------------
  CARGO_TARGET_<TRIPLE>_LINKER    <rust sysroot>/lib/rustlib/<host>/bin/rust-lld
  CARGO_TARGET_<TRIPLE>_RUSTFLAGS -C linker-flavor=ld64.lld -C link-arg=-S
                                  --remap-path-prefix=<CARGO_HOME>/registry/src=/cargo-registry-src
                                  [-C link-arg=-install_name -C link-arg=@rpath/lib<x>.dylib]
  SDKROOT                         the folder holding usr/lib/libSystem.tbd
  MACOSX_DEPLOYMENT_TARGET        11.0
In the container: /opt/codebrix/bin/rust-lld, /opt/codebrix/macos-sysroot and
/usr/local/cargo/registry/src. Both runners take these from MacOSLinkProfile,
so the two packages cannot drift apart.

The profile is OPT-IN in CodeBrix.RustTools (design decision confirmed
2026-10-09): without it, BuildAsync and BuildMacOSUniversalAsync build a
macOS target exactly as "cargo build --target <triple>" does on the machine
(Apple's linker and SDK on a Mac; the only variables the library adds are
RUSTUP_AUTO_INSTALL=0 and CARGO_TERM_COLOR=never, neither affects the
output). A consumer whose container build fails must be able to fall back to
the local library and get what plain cargo gives. The profile exists to
reproduce the container's settings on a Mac for validation. The 2026-10-09
validation built the same four crates (two cdylib plugins, two rlib host
crates) for both Darwin targets with plain cargo, with CodeBrix.RustTools at
default options, with CodeBrix.RustTools plus the profile, and with
CodeBrix.RustTools.Docker: default options reproduced plain cargo, and the
profile reproduced the container.

Accepted limits (unchanged)
---------------------------
* Pure-Rust dependency trees only. A crate that compiles C (the cc crate,
  rusqlite "bundled", aws-lc-rs behind rustls) needs Apple's C headers and
  therefore a Mac.
* Nothing that links an Apple framework beyond libSystem.
* Ad-hoc signatures only. No signing identity, no notarisation, no
  distribution of any kind.
* Of the rust-samples, 13 is the identity test population; 02, 04, 05, 06,
  07, 10 and 12 are pure Rust and build, but 02 and 06 embed a path or a
  build time and so are not byte-identical across setups.

Tooling
-------
The derivation lives only in DockerfileGenerator's macOS layer (one source of
truth; the spike scripts that preceded it were removed on 2026-10-09).
tools/macos-cross/ keeps a README (where the derivation lives, how to export
and inspect a stub with ExportMacOSSysrootAsync, how to add a symbol) and
validate-on-mac.sh, a read-only check of a container-built binary on a Mac
(file, otool -L, codesign -dv, a launch, nm -u against the exported symbol
list). Nothing in it reads Xcode or the SDK.

Phase 4 (in progress)
---------------------
Done on 2026-10-09: Linux x64 / ARM64 / RISC-V 64 from the Docker package on
any engine architecture (LinuxNative + three concrete targets, cross layers,
LinuxLinkProfile); Windows x64 and ARM64 from the Docker package through
Rust's windows-gnullvm targets and llvm-mingw (WindowsGnullvmLinkProfile;
the GCC/msvcrt-based WindowsX64Gnu target was dropped, UCRT is the only
Windows C runtime we link against); Android arm64 / x64 from
CodeBrix.RustTools with a local NDK (AndroidNdkLocator, AndroidLinkProfile,
BuildAndroidAsync). Still open: host-side Linux and Windows cross builds as
conveniences (the profiles exist; a Debian host needs the apt packages the
Linux layer installs, any host needs an llvm-mingw unpacked somewhere).
Bumping llvm-mingw: change DockerfileGenerator.LlvmMingwVersion and both
SHA-256 constants (compute them from the release assets; the project
publishes no checksum file), then re-run the Windows collection.

================================================================================
END OF MAINTAINER-README
