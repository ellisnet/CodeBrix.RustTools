# CodeBrix.RustTools

A .NET library for inspecting, license-checking, building and running Rust applications with the Rust toolchain that is already installed on the local machine. CodeBrix.RustTools is provided as a .NET 10 library and associated `CodeBrix.RustTools.MitLicenseForever` NuGet package.

CodeBrix.RustTools supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.RustTools.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.RustTools`:

* NuGet package ID: `CodeBrix.RustTools.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.RustTools` - i.e. `using CodeBrix.RustTools;`

XML documentation (IntelliSense) ships alongside the assembly.

CodeBrix.RustTools depends on CodeBrix.TomlParse (`CodeBrix.TomlParse.BsdLicenseForever`, used to read Rust's TOML files), which is installed automatically. Note that that package is licensed under the BSD 2-Clause License rather than MIT.

## CodeBrix.RustTools supports:

* Windows, macOS and Debian-based Linux hosts
* Android arm64 and x64 libraries with a locally installed NDK, Linux ARM64 and RISC-V 64 libraries and programs on a Linux host with the GNU cross toolchain, and macOS arm64, x64 and universal binaries on a Mac
* Detecting rustup, cargo, rustc, clippy, rustfmt, rustdoc and the C compiler/linker that Rust needs, with their versions, plus the host triple, active and installed toolchains, targets, components and a project's `rust-toolchain.toml`
* Deciding whether the machine is ready to build - Rust and the C compiler/linker it links with, and for a build for another architecture its standard library and cross tools - before any build starts, and operating-system-specific install and update instructions for anything missing or outdated - returned as data, never executed
* Checking the installed tools against the latest stable Rust and rustup releases, and read-only crates.io lookups
* Reading Cargo projects: `Cargo.toml` (package, dependencies, features, workspaces, targets, profiles), `Cargo.lock`, the resolved `cargo metadata` graph, and finding projects in a folder tree
* A license gate: SPDX license expressions (`OR`, `AND`, `WITH`, `+`) evaluated for every crate in the dependency graph against an allow-list policy, with clear handling of crates whose license cannot be determined - a failed audit stops the build, or, in report-only mode, is attached to the result while the build goes ahead
* Running `cargo build`, `check`, `test`, `clean`, `doc`, `clippy` and `fmt` with typed compiler diagnostics, built artifacts, test counts, progress reporting (with an estimated crate total taken from the build target's dependency graph when a progress reporter is attached), timeouts and cancellation that stops the whole process tree
* Running the programs that cargo built, with arguments, environment, input, timeout and captured output
* Safely extracting zip, tar and tar.gz source archives and building the project they contain in one call
* Dependency health: a dependency tree, crates present in several versions, and outdated or yanked crates

CodeBrix.RustTools never installs or updates anything. When a tool is missing or out of date, it tells the consuming application what the user should run.

## The CodeBrix.RustTools.Docker package

This repository also produces `CodeBrix.RustTools.Docker.MitLicenseForever`, for machines that have a Docker engine but no Rust tools. It builds Rust applications inside a Linux build container with the same cargo commands, license gate and typed results as CodeBrix.RustTools, copies the built programs and libraries back to the host (optionally in the NuGet `runtimes/<rid>/native` layout), and cross-compiles for Windows (GNU toolchain) and Android. It also builds macOS arm64, x64 and universal binaries in the container, for development and testing only.

> **IMPORTANT NOTICE:** macOS binaries produced by CodeBrix.RustTools.Docker are built in Docker containers without Apple's SDK, tools or files and are for development and testing only. They must not be distributed or used in any production environment. Any macOS build that will be distributed or run in production must be produced on a Mac and can be created using CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are available under Apple's own license terms.

See its [README](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/src/CodeBrix.RustTools.Docker/README.md) and [AGENT-README.txt](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/src/CodeBrix.RustTools.Docker/AGENT-README.txt) in `src/CodeBrix.RustTools.Docker`.

## Sample Code

### Check the machine, audit the licenses, then build

```csharp
using CodeBrix.RustTools;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Licensing;

var workbench = new RustWorkbench();

// 1. Can this machine build Rust code?
var toolchain = await workbench.DetectToolchainAsync("/path/to/my-rust-app");
var readiness = workbench.EvaluateReadiness(toolchain);
if (!readiness.IsReady)
{
    foreach (var guidance in workbench.GetInstallGuidance(readiness))
    {
        Console.WriteLine(guidance);   // title, explanation, commands, link
    }

    return;
}

// 2. Open the project and check every dependency's license.
var project = workbench.OpenProject("/path/to/my-rust-app");
var audit = await workbench.AuditLicensesAsync(project, LicensePolicy.Permissive());
Console.WriteLine(audit.ToSummaryText());

// 3. Build (the license gate runs again first), with progress.
var options = new CargoBuildOptions
{
    Profile = CargoProfile.Release,
    LicensePolicy = LicensePolicy.Permissive(),
    ThrowOnLicenseViolation = false,
};
var result = await workbench.BuildAsync(project, options,
    new Progress<CargoProgress>(p => Console.WriteLine(p)));

if (result.LicenseGateFailed)
{
    Console.WriteLine("Not built: " + result.LicenseAudit.ToSummaryText());
}
else if (result.Success)
{
    foreach (var program in result.RunnableArtifacts)
    {
        Console.WriteLine("Built " + program.Executable);
    }
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine(error.Rendered);
    }
}
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.RustTools.Tests` project:
https://github.com/ellisnet/CodeBrix.RustTools/tree/main/tests/CodeBrix.RustTools.Tests

## License

CodeBrix.RustTools is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/THIRD-PARTY-NOTICES.txt).
