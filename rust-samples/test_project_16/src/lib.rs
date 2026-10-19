//! A native module that links dynamically to the CodeBrix WebGPU engine (`codebrix_wgpu`),
//! following route 1 (direct dynamic linking) of NATIVE MODULES in the CodeBrix.WebGpu.Native
//! package's AGENT-README.
//!
//! The whole C ABI comes from the package's `codebrix_wgpu_procs.rs`, pulled in from the
//! folder named by `CODEBRIX_WGPU_INCLUDE_DIR` with no crates. `build.rs` turns on its
//! `direct` module, whose extern block links `codebrix_wgpu`. The library therefore records
//! `libcodebrix_wgpu.so` (Linux, Android), `@rpath/libcodebrix_wgpu.dylib` (macOS) or
//! `codebrix_wgpu.dll` (Windows) as a dependency and exports no engine symbol of its own.

#[allow(non_camel_case_types, non_snake_case, non_upper_case_globals, dead_code)]
mod wgpu_ffi {
    include!(concat!(env!("CODEBRIX_WGPU_INCLUDE_DIR"), "/codebrix_wgpu_procs.rs"));
}

/// The API version of the loaded engine, read through the direct declarations
/// (`wgpuCodebrixGetApiVersion`).
///
/// Call it only after .NET has loaded the engine (`CodeBrixWebGpuNative.EnsureLoaded()`), so
/// the loader reuses that copy.
#[unsafe(no_mangle)]
pub extern "C" fn probe_api_version() -> u32 {
    // SAFETY: wgpuCodebrixGetApiVersion takes no arguments and only returns a constant of the
    // loaded library.
    unsafe { wgpu_ffi::direct::wgpuCodebrixGetApiVersion() }
}

/// The API version this module was built against (`WGPU_CODEBRIX_API_VERSION` of the
/// headers); it does not touch the engine.
#[unsafe(no_mangle)]
pub extern "C" fn probe_built_api_version() -> u32 {
    wgpu_ffi::WGPU_CODEBRIX_API_VERSION
}
