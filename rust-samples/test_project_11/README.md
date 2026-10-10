# test_project_11 - procedural PNG images from a TOML config

Reads `config.toml` (in this folder) with **serde** + **toml**, then uses the **image**
crate to draw three procedural images and save them as PNG files:

* `output/gradient.png` - diagonal two-colour gradient
* `output/mandelbrot.png` - smooth-coloured Mandelbrot set
* `output/plasma.png` - sine-sum plasma

Image size, colours (`#RRGGBB`), Mandelbrot centre / zoom / iterations and plasma
parameters all come from `config.toml`. `output_dir` must be a relative path; absolute
paths and `..` are rejected so the sample can only write inside its own folder.

## Run

```sh
cargo run                      # uses ./config.toml
cargo run -- path/to/other.toml
cargo test                     # in-memory only, writes no files
```

No network access.

## Crates

| Crate | Use |
|-------|-----|
| image (`default-features = false`, `png`) | `RgbImage`, PNG encoding/decoding |
| serde | `Deserialize` for the config structs |
| toml | parsing `config.toml` |

## Project shape

Single binary crate with modules `config.rs` and `render.rs`. The manifest uses a
`[profile.dev.package."*"]` override (`opt-level = 2`) so dependencies are optimized
even in debug builds - a common pattern in image / game projects.
