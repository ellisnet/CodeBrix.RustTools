# test_project_03 - concurrent async HTTP client

An async **tokio** application that uses **reqwest** (rustls TLS, JSON) to call three
public, keyless JSON endpoints at the same time with `tokio::join!`, then prints one
line per result. Logging goes through **tracing** / **tracing-subscriber**.

Endpoints (no API keys, no tokens):

* `https://api.open-meteo.com/v1/forecast` - current weather for Eugene, OR
* `https://api.github.com/repos/rust-lang/rust` - unauthenticated GitHub REST call
* `https://httpbin.org/get` - request echo

## Run

```sh
cargo run
RUST_LOG=debug cargo run   # show request URLs
cargo test                 # offline: only parsing / formatting logic is tested
```

Each failed request is reported and the program keeps going; it exits non-zero only
if every endpoint is unreachable. The program writes no files.

## Crates

| Crate              | Use                                         |
|--------------------|---------------------------------------------|
| tokio (full)       | async runtime, `join!`, `#[tokio::test]`    |
| reqwest            | HTTP client (`default-features = false`, `rustls`, `json`, `http2`, `charset`) |
| serde / serde_json | response deserialization                    |
| tracing            | structured log events                       |
| tracing-subscriber | `fmt` subscriber with `RUST_LOG` env filter |

## Project shape

Single binary crate with two modules (`main.rs`, `sources.rs`). Note that reqwest 0.13
names its rustls feature `rustls` (the 0.12 name was `rustls-tls`); TLS is provided by
rustls with the aws-lc-rs backend, so the build compiles some C code but needs no
OpenSSL development package.
