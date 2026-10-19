# test_project_10 - intentional compiler warnings

**This sample is supposed to produce warnings.** It is a valid program that builds,
runs and passes `cargo test`, but its source deliberately causes many distinct compiler
warnings across two files, plus one build-script warning. It exists so a build tool
(such as CodeBrix.RustTools) can be tested on capturing, parsing and counting warnings.

Please do not fix the warnings, and do not add `#![allow(...)]` - that would defeat the
purpose. (There is exactly one item-level `#[allow(unused_parens)]` to show that a local
allow suppresses a warning on that item only.)

## Warnings produced

| Lint | Where |
|------|-------|
| `unused_imports` | `use std::collections::HashMap;` |
| `unused_variables` | `unused_total` in `main`, `unused_scale` in `shapes.rs` |
| `unused_mut` | `let mut never_mutated` |
| `unused_assignments` | `overwritten = 1` is never read |
| `non_snake_case` | `let BadlyNamed` |
| `non_camel_case_types` | `struct my_counter` |
| `non_upper_case_globals` | `const answer` |
| `dead_code` | `helper_never_called`, `Shape::Square` never constructed, `struct Unused`, `Shape::is_small` |
| `unused_must_use` | `parse_number("123");` discards a `Result` |
| `deprecated` | calls to `#[deprecated] fn old_greet` (also in the test build) |
| `unreachable_code` | expression after `return` in `early_return` |
| `unused_parens` | `if (quiet > 1)` |
| `while_true` | `while true { .. }` |
| `path_statements` | `flag;` |
| `redundant_semicolons` | `;;` |
| `unused_comparisons` | `width >= 0` on a `u8` |
| build-script warning | `build.rs` prints `cargo::warning=...` |

The exact count and wording depend on the rustc version; with rustc 1.94 `cargo build`
reports 20 rustc warnings for the binary target (21 for the test target, which adds
the deprecated call inside a test) plus the build-script warning.

## Run

```sh
cargo build    # succeeds, with warnings
cargo run
cargo test     # passes, with warnings
```

No network access, no files written, no dependencies.
