//! test_project_03: fetches three keyless public JSON endpoints concurrently with
//! `tokio::join!` and prints a one-line summary of each. Writes no files.

mod sources;

use std::process::ExitCode;
use std::time::Duration;

use tracing::{info, warn};
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> ExitCode {
    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")))
        .with_target(false)
        .init();

    let client = match reqwest::Client::builder()
        .user_agent(concat!(env!("CARGO_PKG_NAME"), "/", env!("CARGO_PKG_VERSION")))
        .timeout(Duration::from_secs(15))
        .build()
    {
        Ok(client) => client,
        Err(e) => {
            eprintln!("could not build HTTP client: {e}");
            return ExitCode::FAILURE;
        }
    };

    info!("starting three concurrent requests");
    let (weather, repo, echo) = tokio::join!(
        sources::fetch_weather(&client, 44.05, -123.09),
        sources::fetch_repo(&client, "rust-lang", "rust"),
        sources::fetch_echo(&client),
    );

    let mut failures = 0;
    for (label, result) in [("weather", weather), ("github", repo), ("httpbin", echo)] {
        match result {
            Ok(line) => println!("{label:>8}: {line}"),
            Err(e) => {
                failures += 1;
                warn!(source = label, error = %e, "request failed");
                println!("{label:>8}: unavailable ({e})");
            }
        }
    }

    info!(failures, "done");
    if failures == 3 {
        eprintln!("No endpoint could be reached - is the network available?");
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}
