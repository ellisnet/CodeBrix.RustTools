# test_project_16 - native module linked to the CodeBrix WebGPU engine (cdylib)

A pure-Rust package with **no dependencies** that builds a native shared library which
links **dynamically** to the CodeBrix WebGPU engine, `codebrix_wgpu`, exactly as route 1
(direct dynamic linking) of NATIVE MODULES in the CodeBrix.WebGpu.Native package's
AGENT-README describes. It never bundles an engine of its own.

| Target | Path | Output |
|--------|------|--------|
| lib (`crate-type = ["cdylib"]`, crate `wgpu_link_probe`) | `src/lib.rs` | `libwgpu_link_probe.so` (Linux, Android), `libwgpu_link_probe.dylib` (macOS), `wgpu_link_probe.dll` (Windows) |
| build script | `build.rs` | enables `codebrix_wgpu_direct`, adds the link search path |

The library exports two C-ABI functions, unmangled:

```rust
#[unsafe(no_mangle)]
pub extern "C" fn probe_api_version() -> u32        // wgpuCodebrixGetApiVersion() of the loaded engine
#[unsafe(no_mangle)]
pub extern "C" fn probe_built_api_version() -> u32  // WGPU_CODEBRIX_API_VERSION it was built against
```

## The recipe

`src/lib.rs` takes the whole C ABI from the package's `codebrix_wgpu_procs.rs`, with no
crates. `#[path]` accepts only a literal, so the file is pulled in with `include!` from the
folder named by `CODEBRIX_WGPU_INCLUDE_DIR`:

```rust
#[allow(non_camel_case_types, non_snake_case, non_upper_case_globals, dead_code)]
mod wgpu_ffi {
    include!(concat!(env!("CODEBRIX_WGPU_INCLUDE_DIR"), "/codebrix_wgpu_procs.rs"));
}
```

`build.rs` turns on the file's `direct` module, whose extern block carries
`#[link(name = "codebrix_wgpu")]` (on Windows `kind = "raw-dylib"`, so no import library is
needed), and adds `CODEBRIX_WGPU_LIB_DIR` to the link search path:

```rust
println!("cargo::rustc-check-cfg=cfg(codebrix_wgpu_direct)");
println!("cargo::rustc-cfg=codebrix_wgpu_direct");
println!("cargo::rustc-link-search=native={}", lib_dir);
```

The engine is then called through the direct declarations:

```rust
unsafe { wgpu_ffi::direct::wgpuCodebrixGetApiVersion() }
```

## Build

The two variables name the package's `include/` folder and the folder holding the target's
`codebrix_wgpu` library (here straight from the NuGet cache; CodeBrix.RustTools.CrateBuilder
stages copies into `.codebrix-wgpu/` of its build folder and sets the same variables).
For Linux x64:

```sh
PKG=~/.nuget/packages/codebrix.webgpu.native.mitlicenseforever/<version>
CODEBRIX_WGPU_INCLUDE_DIR=$PKG/include \
CODEBRIX_WGPU_LIB_DIR=$PKG/runtimes/linux-x64/native \
RUSTFLAGS="-L native=$PKG/runtimes/linux-x64/native" \
cargo build --release
```

(`RUSTFLAGS` repeats what `build.rs` does; a crate without a build script needs it, and
`--cfg codebrix_wgpu_direct` too.) For another target add `--target <triple>` and use that
runtime identifier's folder; Windows needs no `-L` flag.

## Load

From .NET, load the engine FIRST so the loader reuses it, then the module:

```csharp
CodeBrixWebGpuNative.EnsureLoaded();
var module = NativeLibrary.Load(".../libwgpu_link_probe.so");
```

The library records `libcodebrix_wgpu.so` (`@rpath/libcodebrix_wgpu.dylib`,
`codebrix_wgpu.dll`) and the loader maps the copy .NET already loaded; exactly one
`libcodebrix_wgpu.so` is mapped in the process.

It is the input of the CodeBrix.RustTools.CrateBuilder WebGPU link tests. Build it in a copy
of this folder (or delete `target/` afterwards); see the repository's EXTRAS-README.txt.
