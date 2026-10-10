# test_project_02 - CSV summarizer CLI

A small command-line tool built with **clap** (derive API) and **anyhow**. It reads the
bundled CSV fixture `data/sales.csv` with the **csv** crate (rows deserialized via
**serde**) and either lists the rows or writes per-region / per-product totals as JSON
(**serde_json**) into this sample's own `output/` folder.

## Run

```sh
cargo run -- list                 # print all rows
cargo run -- list --region north  # filter rows
cargo run -- summarize            # writes output/summary.json
cargo run -- --help
cargo test
```

`--input <file>` selects another CSV file; `summarize --out-dir <dir>` selects another
output folder. By default every path is relative to `CARGO_MANIFEST_DIR`, so the sample
never writes outside its own folder.

## Crates

| Crate      | Use                               |
|------------|-----------------------------------|
| clap       | derive-based argument parsing, subcommands |
| anyhow     | error propagation with context    |
| csv        | CSV reading                       |
| serde      | derive `Deserialize` / `Serialize`|
| serde_json | JSON output                       |

## Project shape

Single binary crate (`src/main.rs`) with unit tests in the same file. No network access.
