//! cargo run --example json_report --features json
//! (declared with `required-features = ["json"]` in Cargo.toml)
fn main() {
    let text = "Call me Ishmael. Some years ago, never mind how long precisely, I thought I would sail about.";
    println!("{}", textkit::json::stats_to_json(&textkit::stats(text)));
}
