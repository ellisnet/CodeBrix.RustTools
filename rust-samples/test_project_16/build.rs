//! Enables the `direct` module of codebrix_wgpu_procs.rs (its extern block carries
//! `#[link(name = "codebrix_wgpu")]`, on Windows `kind = "raw-dylib"`) and adds the folder
//! holding the target's codebrix_wgpu library to the link search path.

fn main() {
    println!("cargo::rerun-if-env-changed=CODEBRIX_WGPU_INCLUDE_DIR");
    println!("cargo::rerun-if-env-changed=CODEBRIX_WGPU_LIB_DIR");
    println!("cargo::rustc-check-cfg=cfg(codebrix_wgpu_direct)");
    println!("cargo::rustc-cfg=codebrix_wgpu_direct");
    if std::env::var_os("CODEBRIX_WGPU_INCLUDE_DIR").is_none() {
        panic!("CODEBRIX_WGPU_INCLUDE_DIR is not set: it must name the include/ folder of the CodeBrix.WebGpu.Native package");
    }
    if let Some(dir) = std::env::var_os("CODEBRIX_WGPU_LIB_DIR") {
        println!("cargo::rustc-link-search=native={}", dir.to_string_lossy());
    }
}
