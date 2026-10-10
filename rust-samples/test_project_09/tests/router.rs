//! Offline integration tests: the router is called directly as a tower Service.

use axum::body::Body;
use axum::http::{Request, StatusCode, header};
use http_body_util::BodyExt;
use serde_json::Value;
use test_project_09::{AppState, Health, SumResponse, router};
use tower::ServiceExt;

async fn call(request: Request<Body>) -> (StatusCode, Value) {
    let response = router(AppState::new()).oneshot(request).await.unwrap();
    let status = response.status();
    let bytes = response.into_body().collect().await.unwrap().to_bytes();
    (status, serde_json::from_slice(&bytes).unwrap())
}

#[tokio::test]
async fn index_lists_routes() {
    let (status, body) = call(Request::get("/").body(Body::empty()).unwrap()).await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(body["service"], "test_project_09");
    assert_eq!(body["routes"].as_array().unwrap().len(), 4);
}

#[tokio::test]
async fn health_reports_ok() {
    let (status, body) = call(Request::get("/health").body(Body::empty()).unwrap()).await;
    assert_eq!(status, StatusCode::OK);
    let health: Health = serde_json::from_value(body).unwrap();
    assert_eq!(health.status, "ok");
    assert_eq!(health.requests_served, 1);
}

#[tokio::test]
async fn hello_uses_path_parameter() {
    let (status, body) = call(Request::get("/api/hello/Ferris").body(Body::empty()).unwrap()).await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(body["greeting"], "Hello, Ferris!");
}

#[tokio::test]
async fn sum_accepts_json() {
    let request = Request::post("/api/sum")
        .header(header::CONTENT_TYPE, "application/json")
        .body(Body::from(r#"{"numbers":[1.5,2.5,5]}"#))
        .unwrap();
    let (status, body) = call(request).await;
    assert_eq!(status, StatusCode::OK);
    let sum: SumResponse = serde_json::from_value(body).unwrap();
    assert_eq!(sum, SumResponse { count: 3, sum: 9.0, mean: Some(3.0) });
}

#[tokio::test]
async fn unknown_route_is_json_404() {
    let (status, body) = call(Request::get("/nope").body(Body::empty()).unwrap()).await;
    assert_eq!(status, StatusCode::NOT_FOUND);
    assert_eq!(body["error"], "no such route");
}
