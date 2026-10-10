# test_project_09 - self-terminating axum JSON server

A small **axum** HTTP server on **tokio** that binds to `127.0.0.1` only, serves a few
JSON routes, and **shuts itself down** after a timeout (default 5 seconds) or on Ctrl-C,
using `axum::serve(..).with_graceful_shutdown(..)`. Running it can never hang forever.

Routes:

| Method | Path                | Response |
|--------|---------------------|----------|
| GET    | `/`                 | service name, version, route list |
| GET    | `/health`           | status, uptime, request count |
| GET    | `/api/hello/{name}` | greeting (axum 0.8 `{param}` path syntax) |
| POST   | `/api/sum`          | `{"numbers":[..]}` -> count / sum / mean |
| *      | anything else       | JSON 404 |

On start-up the program also sends one raw HTTP/1.1 request to its own `/health` route
and prints the response, so a plain run shows the server actually working.

## Run

```sh
cargo run                                   # ephemeral port, stops after 5 s
cargo run -- --port 8089 --timeout 30       # fixed port, stops after 30 s
curl http://127.0.0.1:8089/api/hello/you    # while it is running
cargo test
```

`--timeout` accepts 1..=3600 seconds. No outbound network access and no files written.

## Tests

`tests/router.rs` drives the `Router` directly with `tower::ServiceExt::oneshot` and
collects bodies with `http-body-util`, so the tests open no sockets and run offline.

## Crates

| Crate | Use |
|-------|-----|
| axum | routing, extractors (`State`, `Path`, `Json`), `serve` |
| tokio | runtime, TCP listener, `select!`, signals, timers |
| serde / serde_json | request and response bodies |
| tower (dev) | `ServiceExt::oneshot` in tests |
| http-body-util (dev) | `BodyExt::collect` in tests |

## Project shape

Library (`src/lib.rs`: router + handlers) plus binary (`src/main.rs`: socket, shutdown)
in one package, with integration tests in `tests/`.
