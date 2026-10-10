# test_project_12 - batch job with progress bars (every Cargo target kind)

Simulates a batch job: generates N records (UUID v4 ids, chrono timestamps, names,
scores, status enum, tags), writes them as JSON Lines to `output/records.jsonl` inside
this sample's folder while showing **indicatif** progress bars, then reads the file back
and validates it (unique ids, increasing sequence/timestamps, parseable lines).
Logging uses the **log** facade with **env_logger**.

## Cargo targets

This package deliberately contains every standard Cargo target kind:

| Kind | Path | Notes |
|------|------|-------|
| lib | `src/lib.rs` | renamed to `batchjob` via `[lib] name` |
| bin | `src/main.rs` | the batch job itself |
| example | `examples/single_record.rs` | prints one record |
| test | `tests/round_trip.rs` | integration test (in memory) |
| bench | `benches/record_throughput.rs` | **criterion** benchmark, `harness = false` |

Unit tests live in `src/lib.rs`.

## Run

```sh
cargo run                           # 20,000 records
cargo run --release -- --count 500000 --seed 1
RUST_LOG=debug cargo run
cargo run --example single_record
cargo test
cargo bench                         # criterion results go under target/criterion
```

No network access. Output: `output/records.jsonl`.

## Crates

| Crate | Use |
|-------|-----|
| serde / serde_json | record (de)serialization, JSONL |
| chrono (`serde`) | `DateTime<Utc>` timestamps |
| uuid (`v4`, `serde`) | random record ids |
| indicatif | `MultiProgress` progress bars |
| log / env_logger | logging controlled by `RUST_LOG` |
| criterion (dev) | benchmarks |
