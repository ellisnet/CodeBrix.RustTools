# test_project_04 - Cargo workspace

A **virtual Cargo workspace** (the root `Cargo.toml` has `[workspace]` but no
`[package]`) with three member crates under `crates/`:

| Member            | Kind    | Purpose |
|-------------------|---------|---------|
| `unitconv_core`   | library | Domain logic: units, quantities, conversion, parsing, error enum (`thiserror`). Has unit tests and an integration test in `tests/`. |
| `unitconv_report` | binary  | Builds a JSON table of every compatible unit conversion and writes it to `output/conversion_table.json` at the workspace root. |
| `unitconv_cli`    | binary  | `clap` command-line converter. The package is `unitconv_cli` but the `[[bin]]` target is renamed to `unitconv`. |

## Notable manifest features

* `resolver = "3"`
* `[workspace.package]` - version / edition / license / publish inherited with `version.workspace = true` etc.
* `[workspace.dependencies]` - shared dependency versions (including the internal
  `unitconv_core` path dependency) inherited with `dep.workspace = true`
* `[profile.release]` defined once at the workspace root
* One shared `Cargo.lock` and `target/` at the workspace root

## License declarations (license-gate fixture)

The members deliberately declare different licenses so a crate-license checker has
something to evaluate:

| Member            | `license` |
|-------------------|-----------|
| `unitconv_core`   | `MIT` (inherited from `[workspace.package]` via `license.workspace = true`) |
| `unitconv_report` | `GPL-3.0-only` |
| `unitconv_cli`    | `MIT OR Apache-2.0` (SPDX expression) |

**The GPL-3.0-only member is intentional**: it is a copyleft license that is expected to
be *outside* a typical permissive allow-list, so a license gate should flag it. It is a
test fixture, not a statement about how this code is licensed.

## Run (from this folder)

```sh
cargo build                              # builds all members
cargo test                               # tests all members
cargo run -p unitconv_report             # writes output/conversion_table.json
cargo run -p unitconv_cli -- 12.5km mi   # or: cargo run --bin unitconv -- 98.6F C
cargo run --bin unitconv -- --list
```

No network access. The only file written is `output/conversion_table.json` inside this folder.

## Crates

anyhow, clap (derive), serde, serde_json, thiserror.
