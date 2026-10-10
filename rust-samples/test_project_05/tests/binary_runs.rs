//! Integration test that runs the compiled `textkit` binary (CARGO_BIN_EXE_<name>).

use std::process::Command;

#[test]
fn binary_prints_stats() {
    let output = Command::new(env!("CARGO_BIN_EXE_textkit")).args(["racecar"]).output().expect("binary runs");
    assert!(output.status.success());
    let stdout = String::from_utf8(output.stdout).unwrap();
    assert!(stdout.contains("palindrome: true"), "{stdout}");
}
