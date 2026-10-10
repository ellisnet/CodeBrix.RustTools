//! The individual endpoints plus the pure formatting logic used by `main`.

use serde::Deserialize;
use tracing::debug;

pub type FetchResult = Result<String, reqwest::Error>;

#[derive(Debug, Deserialize)]
pub struct CurrentWeather {
    pub temperature: f64,
    pub windspeed: f64,
    pub weathercode: u32,
}

#[derive(Debug, Deserialize)]
pub struct WeatherResponse {
    pub current_weather: CurrentWeather,
}

#[derive(Debug, Deserialize)]
pub struct RepoInfo {
    pub full_name: String,
    pub stargazers_count: u64,
    pub forks_count: u64,
    pub language: Option<String>,
}

#[derive(Debug, Deserialize)]
pub struct EchoResponse {
    pub origin: String,
    pub url: String,
}

/// Open-Meteo current conditions (no API key required).
pub async fn fetch_weather(client: &reqwest::Client, lat: f64, lon: f64) -> FetchResult {
    let url = format!("https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true");
    debug!(%url, "GET");
    let body: WeatherResponse = client.get(url).send().await?.error_for_status()?.json().await?;
    Ok(describe_weather(&body.current_weather))
}

/// Unauthenticated GitHub REST call (rate-limited to 60/hour per IP, no token used).
pub async fn fetch_repo(client: &reqwest::Client, owner: &str, repo: &str) -> FetchResult {
    let url = format!("https://api.github.com/repos/{owner}/{repo}");
    debug!(%url, "GET");
    let body: RepoInfo = client.get(url).send().await?.error_for_status()?.json().await?;
    Ok(describe_repo(&body))
}

/// httpbin echo service.
pub async fn fetch_echo(client: &reqwest::Client) -> FetchResult {
    let url = "https://httpbin.org/get?sample=test_project_03";
    debug!(%url, "GET");
    let body: EchoResponse = client.get(url).send().await?.error_for_status()?.json().await?;
    Ok(format!("echoed {} (seen from {})", body.url, body.origin))
}

pub fn weather_code_text(code: u32) -> &'static str {
    match code {
        0 => "clear sky",
        1..=3 => "partly cloudy",
        45 | 48 => "fog",
        51..=57 => "drizzle",
        61..=67 => "rain",
        71..=77 => "snow",
        80..=82 => "rain showers",
        85 | 86 => "snow showers",
        95..=99 => "thunderstorm",
        _ => "unknown conditions",
    }
}

pub fn describe_weather(w: &CurrentWeather) -> String {
    format!("{:.1} C, wind {:.1} km/h, {}", w.temperature, w.windspeed, weather_code_text(w.weathercode))
}

pub fn describe_repo(r: &RepoInfo) -> String {
    format!(
        "{} - {} stars, {} forks, written in {}",
        r.full_name,
        r.stargazers_count,
        r.forks_count,
        r.language.as_deref().unwrap_or("an unknown language")
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn weather_codes_map_to_text() {
        assert_eq!(weather_code_text(0), "clear sky");
        assert_eq!(weather_code_text(63), "rain");
        assert_eq!(weather_code_text(1000), "unknown conditions");
    }

    #[test]
    fn parses_and_describes_weather_json() {
        let json = r#"{"latitude":44.0,"current_weather":{"temperature":12.34,"windspeed":5.0,"weathercode":3,"time":"2026-10-08T12:00"}}"#;
        let parsed: WeatherResponse = serde_json::from_str(json).unwrap();
        assert_eq!(describe_weather(&parsed.current_weather), "12.3 C, wind 5.0 km/h, partly cloudy");
    }

    #[test]
    fn parses_and_describes_repo_json() {
        let json = r#"{"full_name":"a/b","stargazers_count":7,"forks_count":2,"language":null,"extra":true}"#;
        let parsed: RepoInfo = serde_json::from_str(json).unwrap();
        assert_eq!(describe_repo(&parsed), "a/b - 7 stars, 2 forks, written in an unknown language");
    }

    #[tokio::test]
    async fn join_runs_futures_concurrently() {
        use std::time::{Duration, Instant};
        let start = Instant::now();
        let (a, b) = tokio::join!(
            async { tokio::time::sleep(Duration::from_millis(100)).await; 1 },
            async { tokio::time::sleep(Duration::from_millis(100)).await; 2 },
        );
        assert_eq!(a + b, 3);
        assert!(start.elapsed() < Duration::from_millis(190));
    }
}
