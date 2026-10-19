================================================================================
README-INDEX: CodeBrix.RustTools
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below
and read its AGENT-README file in full. Read MAINTAINER-README.txt only if you
are changing this repository itself.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.RustTools.MitLicenseForever - inspect, license-check, build
      and run Rust applications with the Rust toolchain already installed
      on the machine (toolchain detection and install guidance, Cargo
      projects, SPDX license gate, typed cargo results, archives).

  src/CodeBrix.RustTools.Docker/AGENT-README.txt
      CodeBrix.RustTools.Docker.MitLicenseForever - build Rust applications
      inside a Docker container with no local Rust tools (engine guidance,
      build images, license gate, artifact retrieval; cross-compilation for
      Linux x64 / ARM64 / RISC-V 64, Windows x64 / ARM64, Android arm64 /
      x64, and macOS arm64 / x64 / universal for development and testing
      only).

      IMPORTANT NOTICE: macOS binaries produced by
      CodeBrix.RustTools.Docker are built in Docker containers without
      Apple's SDK, tools or files and are for development and testing
      only. They must not be distributed or used in any production
      environment. Any macOS build that will be distributed or run in
      production must be produced on a Mac and can be created using
      CodeBrix.RustTools, where Apple's toolchain, SDK and code signing
      are available under Apple's own license terms.

  src/CodeBrix.RustTools.CrateBuilder/AGENT-README.txt
      CodeBrix.RustTools.CrateBuilder.MitLicenseForever - turn a Rust crate
      (a local Cargo.toml, or a Git repository cloned with CodeBrix.LibGit2)
      into native binaries for chosen runtime identifiers, built with the
      installed toolchain or in Docker, plus a JSON build record and
      optionally a .NET 10 wrapper class library (readiness check first,
      CodeBrix WebGPU linking, C facade generation for plain Rust
      libraries).

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and on the nuget.org page of
      CodeBrix.RustTools.MitLicenseForever.
  src/CodeBrix.RustTools.Docker/README.md
      The nuget.org page of CodeBrix.RustTools.Docker.MitLicenseForever.
  src/CodeBrix.RustTools.CrateBuilder/README.md
      The nuget.org page of
      CodeBrix.RustTools.CrateBuilder.MitLicenseForever.
  README-INDEX.txt
      This file.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences. Packed into every
      nupkg.

================================================================================
