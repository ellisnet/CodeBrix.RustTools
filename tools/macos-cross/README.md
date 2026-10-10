# macOS cross-build notes (CodeBrix.RustTools.Docker, Phase 3)

CodeBrix.RustTools.Docker builds `aarch64-apple-darwin` and `x86_64-apple-darwin`
inside a Linux container **without Apple's SDK and without redistributing anything
Apple created**: Rust's own precompiled Darwin standard libraries, rustup's
`rust-lld`, and a libSystem symbol stub that the image derives for itself.
`MAINTAINER-README.txt` (section "PHASE 3: macOS CROSS-BUILD FROM LINUX") records
the findings.

> **IMPORTANT NOTICE:** macOS binaries produced by CodeBrix.RustTools.Docker are built in Docker containers without Apple's SDK, tools or files and are for development and testing only. They must not be distributed or used in any production environment. Any macOS build that will be distributed or run in production must be produced on a Mac and can be created using CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are available under Apple's own license terms.

Nothing in this folder ships in a NuGet package or is part of the solution.

## Where the derivation lives

In code, not here: `DockerfileGenerator` (`src/CodeBrix.RustTools.Docker/Images/`)
writes the macOS layer of the image as a Dockerfile heredoc. It lists the symbols
Rust's std rlibs leave undefined (minus what they define), compiles
`DockerfileGenerator.MacOSProbeProgram` (every float and memory operation LLVM
lowers to a C library call) and adds its undefined symbols, appends
`DockerfileGenerator.MacOSStubSupplement` (the ISO C99 `<math.h>` names, the POSIX
`lgamma_r` variants, the Darwin calls LLVM emits), then links the probe against the
stub with `rust-lld` and absorbs whatever the linker still reports, until it links.
The result is `/opt/codebrix/macos-sysroot` in the image. `MacOSLinkProfile` in
CodeBrix.RustTools is the matching link configuration (opt-in on a Mac).

To add a symbol (a crate fails with `rust-lld: error: undefined symbol: _name`):
add the name to `MacOSStubSupplement`, rebuild (the image tag follows the
Dockerfile text, so the next build makes a new image), and run the macOS live
tests. Only names of functions that every macOS since 11.0 exports belong there;
the C standard, POSIX and Apple's public developer documentation are the sources.

## Exporting and inspecting a stub

```csharp
await using var container = await workbench.StartContainerAsync(image);
var sysroot = await workbench.ExportMacOSSysrootAsync(container, "/path/on/host");
```

`usr/lib/libSystem.tbd` is the stub (with `libc`, `libm`, `libpthread`, `libdl`
copies), `symbols-<triple>.txt` the lists it was written from, `README.txt` the
toolchain it came from. Without the library: `docker create` a container from the
image and `docker cp <id>:/opt/codebrix/macos-sysroot .`.

## Checking a binary on a Mac

`validate-on-mac.sh <binary> [symbols-<triple>.txt]` runs read-only checks with
the Mac's own tools: `file` (Mach-O of the expected architecture), `otool -L`
(only `/usr/lib/libSystem.B.dylib` is linked), `codesign -dv` (an ad-hoc,
linker-signed signature; Apple Silicon refuses to launch without one), a launch
with a 10 second limit, and, when a symbols list is given, `nm -u` to confirm
every imported symbol is in the derived list. It copies nothing from Xcode or the
SDK and does not read them.

## Known limits (accepted)

* Pure-Rust dependency trees only. A crate that compiles C (the `cc` crate,
  `rusqlite` with `bundled`, `aws-lc-rs`) needs Apple's C headers and so needs a Mac.
* Nothing that links an Apple framework beyond `libSystem`.
* Ad-hoc signatures only. No identity, no notarisation, no distribution.
* Byte-for-byte identity between a Mac build (with `MacOSLinkProfile`) and a
  container build holds only for dependency trees without build scripts or
  proc-macros; cargo hashes the host into those units' `-C metadata`.
