# test_project_01 - date, time and tomorrow's weather

A small command-line program that prints the current local date and time in a friendly
form ("Thursday of the 8th of October, at 09:15am") together with tomorrow's weather
forecast for Springfield, Oregon: the conditions plus the high and low in °F. The
forecast comes from the free, keyless **Open-Meteo** forecast API
(`https://api.open-meteo.com/v1/forecast`); no account or API key is needed.

When the forecast cannot be fetched (no network, service unavailable) the program still
runs and prints `unable to fetch weather: ...` in place of the forecast.

## Run

```sh
cargo run
cargo test
```

## Crates

| Crate   | Use                                                    |
|---------|--------------------------------------------------------|
| chrono  | local date and time, formatting                        |
| reqwest | blocking HTTPS request to Open-Meteo (`json` feature)  |
| serde   | derive `Deserialize` for the forecast response         |

## Project shape

Single binary crate: `src/main.rs` plus the `infos` module (`src/infos.rs`) with
`date_time` and `weather` sub-modules, each with unit tests in the same file.

This sample needs the network **at run time** (to reach Open-Meteo); the only other
sample that does is `test_project_03` (concurrent async HTTP client). Building it, like
the other samples with registry dependencies, needs the crates either in the local cargo
registry cache or downloadable from crates.io.
