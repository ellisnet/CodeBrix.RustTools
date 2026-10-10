# test_project_05 - library + binary, examples, integration tests, features

One package that contains every common target kind except benches:

* **library** `textkit` (`src/lib.rs`, plus modules `src/case.rs` and `src/json.rs`)
* **binary** `textkit` (`src/main.rs`) that uses the library
* **examples** `examples/basic_stats.rs` and `examples/json_report.rs`
* **integration tests** `tests/library_api.rs` and `tests/binary_runs.rs` (the latter
  runs the built binary via `CARGO_BIN_EXE_textkit`)

The package name is `test_project_05`, but `[lib]` and `[[bin]]` rename both targets to
`textkit`.

## Vendored path dependencies (license-checker fixtures)

`vendor/` holds three one-file local crates, referenced from `[dependencies]` with
`path = "vendor/..."`. Each provides a trivial function the library really calls
(`textkit::vendor_info`), and each exercises an awkward case for a crate-license checker
reading `license` / `license-file` from `cargo metadata`:

| Crate | Manifest | Case it exercises |
|-------|----------|-------------------|
| `vendor/licensed_by_file` | no `license`; `license-file = "LICENSE.txt"` (custom permissive text) | license known only from a file, so `license` is null and `license_file` is set |
| `vendor/unknown_license_id` | `license = "Proprietary-Acme-1.0"` | syntactically valid but non-SPDX identifier |
| `vendor/no_license_at_all` | neither field | missing license metadata entirely |

All three are `publish = false`, which is why Cargo accepts them without license metadata.
The root package itself also declares no license.

## Features

| Feature | Default | Effect |
|---------|---------|--------|
| `case`  | yes     | compiles `textkit::case` (identifier case conversion) |
| `json`  | no      | compiles `textkit::json` and enables the optional `serde` / `serde_json` dependencies (`dep:` syntax) |

The `json_report` example declares `required-features = ["json"]`, so a plain `cargo build`
skips it.

## Run

```sh
cargo run -- "A man, a plan, a canal: Panama"
echo "some text" | cargo run
cargo run --features json -- "HelloWorld"
cargo run --no-default-features -- "plain build"
cargo run --example basic_stats
cargo run --example json_report --features json
cargo test
cargo test --all-features
cargo test --no-default-features
```

No network access, no files written.

## Crates

From crates.io: only `serde` and `serde_json`, and only when the `json` feature is
enabled. Plus the three local `vendor/` crates described above.
