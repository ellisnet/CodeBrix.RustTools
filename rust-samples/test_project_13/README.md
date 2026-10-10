# test_project_13 - native library (cdylib + rlib) and a binary that calls it

A pure-Rust package with **no dependencies** that builds a native shared library other
languages can load, plus a small program that uses the same code as a Rust library.

| Target | Path | Output |
|--------|------|--------|
| lib (`crate-type = ["cdylib", "rlib"]`) | `src/lib.rs` | `libtest_project_13.so` (Linux, Android), `test_project_13.dll` (Windows); plus `libtest_project_13.rlib` |
| bin | `src/main.rs` | `test_project_13` / `test_project_13.exe` |

The library exports one C-ABI function, unmangled:

```rust
#[unsafe(no_mangle)]
pub extern "C" fn cb_add(a: i32, b: i32) -> i32
```

(`#[unsafe(no_mangle)]` is the Rust 2024 edition spelling of `#[no_mangle]`.) The binary
calls `cb_add(2, 3)` through the rlib and prints `test_project_13: cb_add(2, 3) = 5`.
Unit tests in `src/lib.rs` check the sum and the wrap-around on overflow.

From .NET the shared library can be called with P/Invoke:

```csharp
[DllImport("test_project_13")]
static extern int cb_add(int a, int b);
```

It is the input of the CodeBrix.RustTools.Docker tests for artifact retrieval layouts
(`runtimes/<rid>/native`) and for cross-compilation to Windows (GNU) and Android
(arm64-v8a and x86_64).

## Run

```sh
cargo run
cargo test
```

Run it in a copy of this folder (or delete `target/` afterwards); see the repository's
EXTRAS-README.txt.
