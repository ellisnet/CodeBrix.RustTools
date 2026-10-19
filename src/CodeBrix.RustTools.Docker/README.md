# CodeBrix.RustTools.Docker

A .NET library for building Rust applications inside a Linux Docker container, so the machine needs a running Docker engine but no Rust tools. CodeBrix.RustTools.Docker is provided as a .NET 10 library and associated `CodeBrix.RustTools.Docker.MitLicenseForever` NuGet package.

CodeBrix.RustTools.Docker supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.RustTools.Docker.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.RustTools.Docker`:

* NuGet package ID: `CodeBrix.RustTools.Docker.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.RustTools.Docker` - i.e. `using CodeBrix.RustTools.Docker;`

XML documentation (IntelliSense) ships alongside the assembly.

CodeBrix.RustTools.Docker depends on CodeBrix.RustTools (`CodeBrix.RustTools.MitLicenseForever`, the sibling library that works with a locally installed Rust toolchain) and CodeBrix.Docker (`CodeBrix.Docker.MitLicenseForever`, which talks to the Docker engine); both are installed automatically. CodeBrix.RustTools in turn brings CodeBrix.TomlParse (`CodeBrix.TomlParse.BsdLicenseForever`), which is licensed under the BSD 2-Clause License rather than MIT.

At run time the library needs a Docker engine running Linux containers - Docker Desktop on Windows and macOS, Docker Engine on Linux, or a remote engine reached over SSH. Building a customized build image also needs the `docker` command-line tool with its buildx plugin.

## CodeBrix.RustTools.Docker supports:

* Checking that the Docker engine is reachable and runs Linux containers, with its version and architecture - and operating-system-specific instructions (install Docker Desktop or Docker Engine, start it, grant the current user access, enable WSL 2, switch to Linux containers) returned as data, never executed
* Preparing build images from the official `rust` image, optionally with generated Dockerfile layers for extra Debian packages, clippy and rustfmt, a pinned Rust toolchain, Windows and Android cross-compilation, and custom instructions - the generated Dockerfile is deterministic and the derived image is tagged with its hash, so identical specifications build once and are reused (the generated layers are Debian-based)
* Bringing your own Containerfile as the base of the build image - for example a manylinux / AlmaLinux image with its own toolchain - built once per content hash (Containerfile, build arguments and context files) and used as it is or under the generated layers
* Starting build containers with persistent cargo registry and git cache volumes and a separate target volume, environment variables, resource limits and extra mounts - reused by settings, kept or removed on disposal - optionally with no network at all, so dependencies fetched in one container are compiled offline in another
* Running commands such as cargo and rustup inside a build container, with the exit code and both output streams reported on failure
* Copying a project folder - or a zip / tar / tar.gz source archive, extracted on the host - into the container through the Docker Engine API (no shared folders, so remote engines work too), skipping `.git`, `target`, `node_modules` and your own patterns
* Running cargo build, check, test, doc, clippy, fmt, clean and fetch inside the container with `--message-format=json`, returning the same typed results as CodeBrix.RustTools - compiler diagnostics, artifacts, test counts and live progress - with cancellation that kills every compiler process in the container
* Applying a CodeBrix.RustTools license policy inside the container (`cargo metadata` runs there) before anything compiles, either throwing or returning a failed result with the audit report - or, in report-only mode, attaching the audit report to a build that goes ahead
* Copying built executables and libraries out to the host - flat, per target, or in the NuGet `runtimes/<rid>/native` layout - with size, SHA-256 and the Unix execute bit
* Doing all of the above in one `BuildAsync` call, for several targets at once, with stage-by-stage progress, timing, and the container removed or kept for fast rebuilds
* Labelling every container, image and volume the library creates, and removing exactly those again - optionally only what one workbench instance created; a workbench with its own label prefix also gets its own derived-image repository, so separate workbenches never share or remove each other's images
* Building and running images of another architecture under emulation (`BuildImageSpec.Platform`, for example `linux/arm64` or `linux/riscv64` on an x86-64 engine) - the platform goes to the base image pull, the image build and the build container, and is part of the image and container identity - with a probe that tells whether the engine can run that platform and, when it cannot, how to register the qemu-user binfmt handlers (on Debian `sudo apt install qemu-user-static binfmt-support`; never run by the package). Emulated builds are many times slower than native ones
* Optionally pruning the BuildKit build cache that derived-image builds leave behind (`CleanupOptions.PruneBuildCache`) - removing images does not reclaim it, and the prune is engine-wide (it clears every project's unused build cache, not only this library's), so it is off by default

CodeBrix.RustTools.Docker never installs, starts or configures Docker. When the engine cannot be used, it tells the consuming application what the user should do.

## Cross-compilation targets

* **Linux x64, ARM64 and RISC-V 64** - the engine's own architecture natively (`LinuxNative`, the default); the other two cross-compiled with Debian's cross GCC and glibc packages, so an ARM64 engine builds x86-64 and RISC-V binaries and an x86-64 engine builds ARM64 and RISC-V ones, without emulation.
* **Windows x64 and ARM64** - Rust's windows-gnullvm targets built with llvm-mingw (clang, lld and the mingw-w64 runtime, all open source) against Windows' Universal C Runtime (Windows 10 and later): `.exe` and `.dll` files that import only Windows' own DLLs. No Microsoft SDK, compiler or runtime is downloaded; works on x64 and ARM64 engines alike.
* **Android arm64 and x64** - built with the Android NDK's clang for API level 24 by default (`ContainerBuildRequest.AndroidApiLevel` picks another level): per-ABI `.so` files. The NDK is fetched at your request under the [Android Software Development Kit License Agreement](https://developer.android.com/studio/terms): it is downloaded from Google only when you build an image with an Android target, and this package does not redistribute it. The NDK's compilers run on x86-64 Linux only, so Android builds need an x86-64 Docker engine; on any other engine (for example Docker Desktop on Apple Silicon) they throw `CrossTargetNotSupportedException`.
* **macOS arm64, x64 and universal** - *development and testing only*: Rust's own Darwin standard libraries, linked by rustup's `rust-lld` against a libSystem symbol stub the image derives from those libraries. Nothing from Apple is downloaded, copied or needed, and it works on x64 and ARM64 engines alike; `MacOSUniversal` joins the two slices into one fat file. The same settings on a Mac (`CodeBrix.RustTools.MacOSLinkProfile`) give byte-for-byte the same files. Pure-Rust crates only; ad-hoc signatures only.

With the `NuGetRuntimes` layout the built libraries land in `runtimes/linux-x64/native` (`runtimes/linux-arm64/native` when the Docker engine is ARM64, for example Docker Desktop on Apple Silicon: the Linux target is the engine's own platform), `runtimes/win-x64/native`, `runtimes/android-arm64/native` and `runtimes/android-x64/native`, ready to be packed into a NuGet package (`<None Include="native/runtimes/**" Pack="true" PackagePath="runtimes/" />`). The macOS native libraries of a published package must be built on a Mac with CodeBrix.RustTools.

> **IMPORTANT NOTICE:** macOS binaries produced by CodeBrix.RustTools.Docker are built in Docker containers without Apple's SDK, tools or files and are for development and testing only. They must not be distributed or used in any production environment. Any macOS build that will be distributed or run in production must be produced on a Mac and can be created using CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are available under Apple's own license terms.

## Sample Code

### Build a Rust library for Linux, Windows and Android in one call

```csharp
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Images;

using var workbench = new DockerRustWorkbench();

var result = await workbench.BuildAsync(
    new ContainerBuildRequest
    {
        ProjectDirectory = "/home/me/my-native-lib",   // a cdylib crate
        Targets = new List<CrossTarget>
        {
            CrossTarget.LinuxX64,
            CrossTarget.WindowsX64Gnullvm,
            CrossTarget.WindowsArm64Gnullvm,
            CrossTarget.AndroidArm64,
            CrossTarget.AndroidX64,
        },
        Retrieval = new ArtifactRetrievalOptions
        {
            HostDirectory = "/home/me/my-package/native",
            Layout = ArtifactLayout.NuGetRuntimes,
        },
    },
    new Progress<ContainerBuildProgress>(p => Console.WriteLine(p)));

Console.WriteLine(result);   // the outcome per target
foreach (var file in result.Retrieval?.Artifacts ?? [])
{
    Console.WriteLine($"{file.HostPath}  {file.Sha256}");
}
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.RustTools.Docker.Tests` project:
https://github.com/ellisnet/CodeBrix.RustTools/tree/main/tests/CodeBrix.RustTools.Docker.Tests

## License

CodeBrix.RustTools.Docker is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/THIRD-PARTY-NOTICES.txt).
