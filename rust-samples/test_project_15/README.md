# test_project_15 - plain Rust library (rlib) with a data-shaped public API

A pure-Rust package named `datacrate` with **no dependencies** and **no C exports**: it builds
only an rlib, so another language can call it only through a generated C facade. It is the input
of the CodeBrix.RustTools.CrateBuilder tests for API extraction, mapping, facade and wrapper
generation.

| Item | Shape |
|------|-------|
| `Vec2 { x, y }` | `Copy` struct of `f64` fields with `new`, `dot`, `length`, `lerp` |
| `Tick(pub i64)` | `Copy` newtype with `from_seconds_f64` / `seconds` (ticks per second = 254016000000) |
| `Rect` (in `pub mod shapes`, re-exported at the root) | `Copy` struct with `from_size`, `width`, `union(&Rect)` |
| `Mode { A = 1, B = 2 }` | C-like enum with `from_name(&str) -> Option<Mode>` |
| `Encoder` | non-`Copy` handle type: `new(EncoderConfig) -> Self`, `encode(&mut self, &[u8]) -> Vec<u8>`, `frames(&self) -> u32` |
| `ParseError` | C-like enum implementing `Display` and `Error` |
| free functions | `checksum(&[u8]) -> u32`, `greet(&str) -> String`, `parse_pair(&str) -> Result<(i32, i32), ParseError>`, `invert([[f64; 3]; 3]) -> [[f64; 3]; 3]`, `find(&[i32], i32) -> Option<usize>` |
| not public | one `pub(crate)` function, one private function, the private `time` module (its items are re-exported) |
| `wasm_only()` | behind `#[cfg(target_arch = "wasm32")]` |

Every public item has a doc comment. Unit tests in `src/lib.rs` and `src/time.rs` make
`cargo test` meaningful.

## Run

```sh
cargo test
```

Run it in a copy of this folder (or delete `target/` afterwards); see the repository's
EXTRAS-README.txt.
