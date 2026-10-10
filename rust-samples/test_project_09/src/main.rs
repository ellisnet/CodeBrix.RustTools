//! test_project_09: serves JSON on 127.0.0.1 and stops by itself.
//!
//! Usage: test_project_09 [--port N] [--timeout SECS]
//!   --port     0 (default) picks a free ephemeral port
//!   --timeout  seconds before automatic shutdown, default 5 (1..=3600)
//! Ctrl-C also shuts the server down gracefully.

use std::net::{Ipv4Addr, SocketAddr};
use std::process::ExitCode;
use std::time::Duration;

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

struct Options {
    port: u16,
    timeout: Duration,
}

fn parse_args() -> Result<Options, String> {
    let mut opts = Options { port: 0, timeout: Duration::from_secs(5) };
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        let value = args.next().ok_or_else(|| format!("{arg} needs a value"))?;
        match arg.as_str() {
            "--port" => opts.port = value.parse().map_err(|_| format!("bad port '{value}'"))?,
            "--timeout" => {
                let secs: u64 = value.parse().map_err(|_| format!("bad timeout '{value}'"))?;
                if !(1..=3600).contains(&secs) {
                    return Err("--timeout must be between 1 and 3600 seconds".into());
                }
                opts.timeout = Duration::from_secs(secs);
            }
            other => return Err(format!("unknown argument {other}")),
        }
    }
    Ok(opts)
}

async fn shutdown_signal(timeout: Duration) {
    tokio::select! {
        _ = tokio::time::sleep(timeout) => println!("timeout of {}s reached, shutting down", timeout.as_secs()),
        result = tokio::signal::ctrl_c() => match result {
            Ok(()) => println!("ctrl-c received, shutting down"),
            // No signal handler available: fall back to the timeout alone.
            Err(_) => tokio::time::sleep(timeout).await,
        },
    }
}

/// Issues one raw HTTP/1.1 request against the running server to show it works.
async fn self_check(addr: SocketAddr) -> std::io::Result<String> {
    let mut stream = TcpStream::connect(addr).await?;
    stream.write_all(b"GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n").await?;
    let mut response = String::new();
    stream.read_to_string(&mut response).await?;
    let status = response.lines().next().unwrap_or_default().to_string();
    let body = response.split("\r\n\r\n").nth(1).unwrap_or_default().to_string();
    Ok(format!("{status} {body}"))
}

#[tokio::main]
async fn main() -> ExitCode {
    let opts = match parse_args() {
        Ok(opts) => opts,
        Err(msg) => {
            eprintln!("{msg}\nusage: test_project_09 [--port N] [--timeout SECS]");
            return ExitCode::from(2);
        }
    };

    let listener = match TcpListener::bind((Ipv4Addr::LOCALHOST, opts.port)).await {
        Ok(listener) => listener,
        Err(e) => {
            eprintln!("cannot bind 127.0.0.1:{}: {e}", opts.port);
            return ExitCode::FAILURE;
        }
    };
    let addr = listener.local_addr().expect("bound socket has an address");
    println!("listening on http://{addr} (auto shutdown in {}s)", opts.timeout.as_secs());

    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(200)).await;
        match self_check(addr).await {
            Ok(line) => println!("self-check: {line}"),
            Err(e) => println!("self-check failed: {e}"),
        }
    });

    let app = test_project_09::router(test_project_09::AppState::new());
    if let Err(e) = axum::serve(listener, app).with_graceful_shutdown(shutdown_signal(opts.timeout)).await {
        eprintln!("server error: {e}");
        return ExitCode::FAILURE;
    }
    println!("server stopped");
    ExitCode::SUCCESS
}
