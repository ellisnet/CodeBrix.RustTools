use serde::Deserialize;

#[derive(Deserialize)]
struct WeatherResponse {
    daily: DailyWeather,
}

#[derive(Deserialize)]
struct DailyWeather {
    temperature_2m_max: Vec<f64>,
    temperature_2m_min: Vec<f64>,
    weather_code: Vec<i32>,
}

fn weather_code_to_description(code: i32) -> &'static str {
    match code {
        0 => "clear sky",
        1..=3 => "partly cloudy",
        45 | 48 => "foggy",
        51 | 53 | 55 => "drizzle",
        56 | 57 => "freezing drizzle",
        61 | 63 | 65 => "rain",
        66 | 67 => "freezing rain",
        71 | 73 | 75 => "snow",
        77 => "snow grains",
        80..=82 => "rain showers",
        85 | 86 => "snow showers",
        95 => "thunderstorm",
        96 | 99 => "thunderstorm with hail",
        _ => "unknown conditions",
    }
}

fn format_tomorrow_weather(response: &WeatherResponse) -> Result<String, &'static str> {
    if response.daily.temperature_2m_max.len() <= 1
        || response.daily.temperature_2m_min.len() <= 1
        || response.daily.weather_code.len() <= 1
    {
        return Err("weather response missing tomorrow data");
    }

    let high = response.daily.temperature_2m_max[1];
    let low = response.daily.temperature_2m_min[1];
    let weather_code = response.daily.weather_code[1];
    let description = weather_code_to_description(weather_code);

    Ok(format!(
        "{} with a high of {:.0}°F and low of {:.0}°F",
        description, high, low
    ))
}

pub fn get_tomorrow_weather() -> Result<String, Box<dyn std::error::Error>> {
    // Coordinates for zip code 97478 (Springfield, Oregon)
    let latitude = 44.0462;
    let longitude = -122.7566;

    let url = format!(
        "https://api.open-meteo.com/v1/forecast?latitude={}&longitude={}&daily=temperature_2m_max,temperature_2m_min,weather_code&temperature_unit=fahrenheit&timezone=America/Los_Angeles&forecast_days=2",
        latitude, longitude
    );

    let response: WeatherResponse = reqwest::blocking::get(&url)?.json()?;

    format_tomorrow_weather(&response)
        .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e).into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_tomorrow_weather_from_response() {
        let response = WeatherResponse {
            daily: DailyWeather {
                temperature_2m_max: vec![55.0, 60.0],
                temperature_2m_min: vec![40.0, 42.0],
                weather_code: vec![1, 1],
            },
        };

        let formatted = format_tomorrow_weather(&response).unwrap();
        assert_eq!(
            formatted,
            "partly cloudy with a high of 60°F and low of 42°F"
        );
    }

    #[test]
    fn returns_error_when_missing_tomorrow_data() {
        let response = WeatherResponse {
            daily: DailyWeather {
                temperature_2m_max: vec![55.0],
                temperature_2m_min: vec![40.0],
                weather_code: vec![0],
            },
        };

        let error = format_tomorrow_weather(&response).unwrap_err();
        assert_eq!(error, "weather response missing tomorrow data");
    }
}
