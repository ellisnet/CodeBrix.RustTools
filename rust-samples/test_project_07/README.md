# test_project_07 - parallel word frequencies (rayon + regex)

Counts word frequencies in a public-domain text using **rayon** for data parallelism
and **regex** for Unicode-aware tokenizing. Compiled regexes and the stop-word set are
global `std::sync::LazyLock` statics (the standard-library successor to `once_cell::sync::Lazy`).

Paragraphs are counted in parallel (`par_iter().map(..).reduce(..)`) and the final list is
sorted with `par_sort_unstable_by`.

The fixture `data/moby_dick_loomings.txt` is an excerpt from Chapter 1 of Herman
Melville's *Moby-Dick* (1851), public domain, from Project Gutenberg eBook #2701. It
contains UTF-8 punctuation (em dashes, curly apostrophes).

## Run

```sh
cargo run --release
cargo run -- --top 10
cargo run -- --input some/other.txt --out-dir some/dir
cargo test
```

Output (default `output/` inside this folder):

* `output/word_frequencies.csv` - every word and its count
* `output/report.txt` - totals, detected proper nouns, top N content words

No network access.

## Crates

| Crate | Use |
|-------|-----|
| rayon | parallel iterators, parallel sort |
| regex | tokenizer and proper-noun pattern |
| dirs | `dirs::home_dir()`, printed for information only (nothing is written there) |

### License note (license-gate fixture)

`dirs` itself is `MIT OR Apache-2.0`, but it depends on `dirs-sys`, which depends on
`option-ext`, licensed **MPL-2.0** - a weak-copyleft license arriving transitively. This is
deliberate, so a crate-license checker that walks `cargo metadata` sees a realistic
"surprise" copyleft dependency in an otherwise permissive tree.

## Project shape

Single binary crate; also sets a `[profile.release]` override (`codegen-units = 1`).
