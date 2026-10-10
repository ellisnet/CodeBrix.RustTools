# test_project_08 - SQLite catalog (rusqlite bundled + thiserror)

Creates a SQLite database **inside this sample's folder** (`output/catalog.db`), creates
tables and an index, inserts a small catalog of 19th-century novels inside transactions,
updates a row, and runs join / aggregate queries. Errors are modelled with a
**thiserror** enum (`src/error.rs`) that wraps `rusqlite::Error` and `std::io::Error` and
adds domain variants.

## Build notes

`rusqlite` is used with the `bundled` feature, so `libsqlite3-sys` compiles the SQLite
amalgamation (a large C file) with the `cc` crate during the build. No system SQLite
library or headers are needed, but a C compiler is, and the first build takes noticeably
longer than a pure-Rust crate. This is intentional: it gives a build tool a realistic
"build script compiling C" phase to observe.

## Run

```sh
cargo run      # (re)creates output/catalog.db and prints query results
cargo test     # uses in-memory databases only
```

Running it again clears and repopulates the same database file. No network access.

## Crates

| Crate     | Use |
|-----------|-----|
| rusqlite (`bundled`) | SQLite access; compiles SQLite from source |
| thiserror | `#[derive(Error)]` for `CatalogError` |

## Project shape

Single binary crate with modules `catalog.rs` and `error.rs`.
