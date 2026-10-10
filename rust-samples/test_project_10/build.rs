// Build script whose only job is to emit a build-script warning, which Cargo prints as
// `warning: test_project_10@0.1.0: ...` - a different shape from rustc warnings.
fn main() {
    println!("cargo::rerun-if-changed=build.rs");
    println!("cargo::warning=this is an intentional build-script warning from test_project_10");
}
