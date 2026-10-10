================================================================================
EXTRAS-README: CodeBrix.RustTools
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

This repository ships no sample .NET applications, demos or command-line
tools. Two projects are packable - src/CodeBrix.RustTools and
src/CodeBrix.RustTools.Docker - and everything else listed below exists only
to test, document or plan them. None of it is included in either package.

For runnable, compilable usage of the libraries, read the test projects: the
"WORKING EXAMPLES ON GITHUB" section of each AGENT-README maps each feature
area to the test file that exercises it.


TEST PROJECTS
=============
    tests/CodeBrix.RustTools.Tests/
    tests/CodeBrix.RustTools.Docker.Tests/

The only non-package projects in the solution. xUnit v3; run them as
described in MAINTAINER-README.txt (TESTING); the Docker suite needs a
running Docker engine. The first project's Fixtures/ folder holds captured
cargo, rustc and libtest output and trimmed copies of the Rust release
manifests and crates.io API responses used by the parser and stubbed-HTTP
tests (see THIRD-PARTY-NOTICES.txt).


RUST SAMPLES
============
    rust-samples/test_project_01 ... test_project_13

Thirteen small, self-contained Rust projects (each with its own README.md,
Cargo.toml and Cargo.lock) that give the library real inputs: every target
kind, a workspace, build scripts, features, license-gate fixtures and
deliberate warnings. They are not part of the .NET solution and are not
packaged.

  01  binary; local date/time + tomorrow's weather from the keyless
      Open-Meteo API (chrono, reqwest, serde); needs the network at run time
  02  binary CLI; CSV summary to JSON (clap, anyhow, csv, serde_json); the
      "real dependencies" build
  03  binary; concurrent async HTTP client calling three public keyless
      endpoints (tokio, reqwest with rustls, tracing); network at run time
  04  virtual workspace, three members (library + two binaries),
      [workspace.package] / [workspace.dependencies] inheritance; license
      fixture: members are MIT, GPL-3.0-only and MIT OR Apache-2.0
  05  library + binary + examples + integration tests + features; license
      fixtures in vendor/: a crate with only license-file, one with the
      unknown id Proprietary-Acme-1.0, one with no license at all
  06  build.rs code generation, two explicit [[bin]] targets, autobins off
  07  binary; parallel word frequencies (rayon, regex) over a Moby-Dick
      excerpt; license fixture: transitive MPL-2.0 crate (option-ext via
      dirs)
  08  binary; SQLite catalog (rusqlite "bundled" compiles C, thiserror)
  09  library + binary; self-terminating axum JSON server on 127.0.0.1
  10  binary + build script that deliberately produce many distinct compiler
      warnings and one build-script warning (do not fix them)
  11  binary; procedural PNG images from a TOML config (image, toml, serde)
  12  every Cargo target kind (lib, bin, example, test, bench); batch job
      with progress bars (indicatif, uuid, chrono, log)
  13  native library: [lib] crate-type = ["cdylib", "rlib"] exporting the
      C-ABI function cb_add (#[unsafe(no_mangle)] pub extern "C"), and a
      binary that calls it through the rlib; pure Rust, no dependencies,
      MIT. Builds libtest_project_13.so on Linux and Android and
      test_project_13.dll (+ .exe) on Windows: the input of the Docker
      suite's retrieval-layout and cross-compilation tests

Which ones the tests use: 02, 04, 05, 06 and 10 are built (05 most of all);
07 is read with `cargo metadata` only; the others are read-only parsing
inputs at most. The Docker suite builds 02, 04, 05, 06, 10 and 13 inside
containers, 13 also for Windows (GNU) and Android. 03, 08, 09 and 11 are
never built by the tests (slow C, TLS, async-runtime or image builds).

The sandbox rule: tests never run cargo inside rust-samples/. They copy a
sample to a sandbox under the test output folder first, so no target/ or
output/ folder is ever created here. When you run a sample by hand, do it in
a copy, or delete target/ and output/ afterwards (each sample's .gitignore
lists /target).


TOOLS
=====
    tools/macos-cross/

Maintainer notes for Phase 3 (macOS cross-compilation from Linux, a feature
of CodeBrix.RustTools.Docker): a README that says where the libSystem stub
derivation lives (DockerfileGenerator's macOS layer), how to export and
inspect a stub, and how to add a symbol; and validate-on-mac.sh, a read-only
check of a container-built binary on a Mac (file, otool, codesign, launch,
nm against the exported symbol list). It installs nothing, reads nothing
from Xcode or the SDK, contains no Apple content, and is not part of the
solution or any package. MAINTAINER-README.txt (section "PHASE 3: macOS
CROSS-BUILD FROM LINUX") records the findings.

IMPORTANT NOTICE: macOS binaries produced by CodeBrix.RustTools.Docker are
built in Docker containers without Apple's SDK, tools or files and are for
development and testing only. They must not be distributed or used in any
production environment. Any macOS build that will be distributed or run in
production must be produced on a Mac and can be created using
CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are
available under Apple's own license terms.


SCRATCH HARNESSES
=================
A scratch console harness that builds third-party Rust repositories inside
containers lives OUTSIDE this repository (under the maintainer's
~/ClaudeHome) and is not part of it. It references
src/CodeBrix.RustTools.Docker as a scratch-only ProjectReference; nothing in
this repository references it, and none of the code it builds, nor its
results, ever enter this repository. Such third-party code is built only in
a dedicated container with its own volumes that is destroyed afterwards,
never on the host.
