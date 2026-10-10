# test_project_06 - build script code generation + multiple binaries

Demonstrates a `build.rs` build script and a package with two explicit `[[bin]]` targets
(and `autobins = false`, so there is no default `src/main.rs` binary). The binary sources
live in `src/programs/` instead of the conventional `src/bin/`, which keeps them clear of
blanket `bin` ignore rules in repositories that carry this sample.

`build.rs` runs before the crate compiles and writes `$OUT_DIR/generated.rs` containing:

* build metadata: package version, profile, target triple, build time (Unix seconds),
  and a combined version string - no git required
* a 256-entry CRC-32 lookup table computed at build time
* a `PLANETS` array parsed from `data/planets.txt`

It uses the `cargo::rerun-if-changed=` directive (so editing `data/planets.txt` re-runs it)
and can emit a `cargo::warning=` if the data file looks short. Both binaries pull in the
generated code with `include!(concat!(env!("OUT_DIR"), "/generated.rs"))`.

| Binary      | Purpose |
|-------------|---------|
| `buildinfo` | prints the generated metadata and planet table |
| `crc32sum`  | prints CRC-32 checksums of files (defaults to `data/planets.txt`) |

## Run

```sh
cargo run --bin buildinfo
cargo run --bin crc32sum
cargo run --bin crc32sum -- Cargo.toml README.md
cargo test
```

Because there are two binaries, a plain `cargo run` asks you to pick one with `--bin`.

No network access; the only file written is the generated source inside `target/`.

## Crates

None - standard library only.
