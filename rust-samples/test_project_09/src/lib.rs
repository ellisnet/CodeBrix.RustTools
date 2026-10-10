//! Router and handlers, kept in a library so integration tests can drive the router
//! directly with `tower::ServiceExt::oneshot` without opening a socket.

use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{Instant, SystemTime, UNIX_EPOCH};

use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};

#[derive(Debug)]
pub struct AppState {
    started: Instant,
    requests: AtomicU64,
}

impl AppState {
    pub fn new() -> Arc<Self> {
        Arc::new(Self { started: Instant::now(), requests: AtomicU64::new(0) })
    }
}

#[derive(Debug, Serialize, Deserialize, PartialEq)]
pub struct Health {
    pub status: String,
    pub uptime_ms: u128,
    pub requests_served: u64,
}

#[derive(Debug, Deserialize)]
pub struct SumRequest {
    pub numbers: Vec<f64>,
}

#[derive(Debug, Serialize, Deserialize, PartialEq)]
pub struct SumResponse {
    pub count: usize,
    pub sum: f64,
    pub mean: Option<f64>,
}

pub fn router(state: Arc<AppState>) -> Router {
    Router::new()
        .route("/", get(index))
        .route("/health", get(health))
        .route("/api/hello/{name}", get(hello))
        .route("/api/sum", post(sum))
        .fallback(not_found)
        .with_state(state)
}

async fn index(State(state): State<Arc<AppState>>) -> Json<Value> {
    state.requests.fetch_add(1, Ordering::Relaxed);
    Json(json!({
        "service": env!("CARGO_PKG_NAME"),
        "version": env!("CARGO_PKG_VERSION"),
        "routes": ["GET /", "GET /health", "GET /api/hello/{name}", "POST /api/sum"],
    }))
}

async fn health(State(state): State<Arc<AppState>>) -> Json<Health> {
    let served = state.requests.fetch_add(1, Ordering::Relaxed) + 1;
    Json(Health { status: "ok".into(), uptime_ms: state.started.elapsed().as_millis(), requests_served: served })
}

async fn hello(State(state): State<Arc<AppState>>, Path(name): Path<String>) -> Result<Json<Value>, (StatusCode, Json<Value>)> {
    state.requests.fetch_add(1, Ordering::Relaxed);
    if name.len() > 64 {
        return Err((StatusCode::BAD_REQUEST, Json(json!({ "error": "name too long" }))));
    }
    let unix = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    Ok(Json(json!({ "greeting": format!("Hello, {name}!"), "unix_time": unix })))
}

async fn sum(State(state): State<Arc<AppState>>, Json(req): Json<SumRequest>) -> Json<SumResponse> {
    state.requests.fetch_add(1, Ordering::Relaxed);
    let total: f64 = req.numbers.iter().sum();
    let count = req.numbers.len();
    Json(SumResponse { count, sum: total, mean: (count > 0).then(|| total / count as f64) })
}

async fn not_found() -> (StatusCode, Json<Value>) {
    (StatusCode::NOT_FOUND, Json(json!({ "error": "no such route" })))
}
