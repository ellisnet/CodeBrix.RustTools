# CodeBrix.RustTools.CrateBuilder

A .NET library that turns a Rust crate - a local `Cargo.toml`, or a Git repository and the path of a `Cargo.toml` inside it - into native binaries for the .NET runtime identifiers you choose and, optionally, a .NET 10 class library that wraps it. CodeBrix.RustTools.CrateBuilder is provided as a .NET 10 library and associated `CodeBrix.RustTools.CrateBuilder.MitLicenseForever` NuGet package.

CodeBrix.RustTools.CrateBuilder supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.RustTools.CrateBuilder.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.RustTools.CrateBuilder`:

* NuGet package ID: `CodeBrix.RustTools.CrateBuilder.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.RustTools.CrateBuilder` - i.e. `using CodeBrix.RustTools.CrateBuilder;`

XML documentation (IntelliSense) ships alongside the assembly.

CodeBrix.RustTools.CrateBuilder depends on CodeBrix.RustTools and CodeBrix.RustTools.Docker (the sibling packages that build with the local Rust toolchain or inside a Docker container), CodeBrix.LibGit2 (Git clones; its native libgit2 library is licensed under the GPL version 2 with the libgit2 linking exception), CodeBrix.WebGpu.Native (the CodeBrix WebGPU engine) and CodeBrix.Templating (code generation; licensed under the BSD 2-Clause License rather than MIT). All are installed automatically.

## CodeBrix.RustTools.CrateBuilder supports:

* Nine runtime identifiers: `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-riscv64`, `android-x64` and `android-arm64`
* Building with the Rust toolchain installed on the machine or inside a Linux Docker container - one mode per request
* A readiness check before anything runs: which of the requested targets this machine can build in the chosen mode, and what to install for the others - returned as data, never executed
* Local crates (built from a staged copy, so the crate's folder is never written to) and crates cloned from a Git repository at a commit, branch or tag
* Native files in the NuGet `runtimes/<rid>/native` layout, and a JSON build record beside them: the source identity, the toolchain, every target's outcome, file hashes and provenance, and the C functions the library exports
* Plain Rust libraries as well as crates that already export C functions: for a plain library, the crate's public API is read, mapped to C (objects with getters, setters and a constructor for their public fields, Copy structs, C-like enums, strings and paths, slices and vectors of numbers and of plain values, `Option`, `Result`, tuples, fixed arrays, borrowed results as copies, and literal constants as C# `const` values) and compiled into a generated C facade crate with a panic boundary, argument checks and a per-thread error message; a `cdylib` that turns out to export nothing gets a facade in the same build, and `FacadeMode` lets you force either way
* A generated .NET 10 wrapper class library around the native files: objects become `IDisposable` classes, Copy structs become record structs, Rust errors become exceptions, Rust doc comments become XML documentation, and a loader picks the native file for the runtime identifier the application runs on - optionally packable as a NuGet package
* A wrap report (`WRAP-REPORT.txt`) listing everything that was wrapped, everything that was not and why, with a stub for each item you may want to wrap by hand
* Selecting, renaming and overriding parts of the API with a `WrapPlan`
* Linking crates directly against the CodeBrix WebGPU engine (`CodeBrix.WebGpu.Native`), with the link verified after the build
* Clear results: a stage that fails is reported in the result with its stage and message, never as an unexpected exception

CodeBrix.RustTools.CrateBuilder never installs anything. When something is missing, it tells the consuming application what the user should install.

> **IMPORTANT NOTICE:** macOS binaries produced by CodeBrix.RustTools.Docker are built in Docker containers without Apple's SDK, tools or files and are for development and testing only. They must not be distributed or used in any production environment. Any macOS build that will be distributed or run in production must be produced on a Mac and can be created using CodeBrix.RustTools, where Apple's toolchain, SDK and code signing are available under Apple's own license terms. CodeBrix.RustTools.CrateBuilder records such files as development builds.

## Sample Code

### From a GitHub repository to a .NET wrapper project

```csharp
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

Console.WriteLine(result);                    // the outcome for every runtime identifier
Console.WriteLine(result.Wrapper?.ProjectPath); // /work/geo/Acme.Geo/Acme.Geo.csproj - reference it
Console.WriteLine(result.WrapReportPath);      // what was wrapped, what was not, and why
```

### Check which runtime identifiers this machine can build

```csharp
using System;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder;

using var builder = new CrateBuilder();

var report = await builder.CheckReadinessAsync(new CrateBuildRequest
{
    Source = new LocalManifestSource("/home/me/my-crate/Cargo.toml"),
    Mode = BuildMode.Host,
    Targets = CrateTargets.All.ToList(),
    OutputDirectory = "/home/me/my-crate-native",
});

Console.WriteLine(report);   // buildable and unbuildable RIDs, and why
foreach (var target in report.UnbuildableTargets)
{
    foreach (var step in report.GetGuidance(target))
    {
        Console.WriteLine(step);   // what to install - shown, never run
    }
}
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.RustTools.CrateBuilder.Tests` project:
https://github.com/ellisnet/CodeBrix.RustTools/tree/main/tests/CodeBrix.RustTools.CrateBuilder.Tests

## License

CodeBrix.RustTools.CrateBuilder is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.RustTools/blob/main/THIRD-PARTY-NOTICES.txt).
