mod infos;

fn main() {
    let datetime = infos::date_time::get_formatted_datetime();
    let weather = infos::weather::get_tomorrow_weather().unwrap_or_else(|e| format!("unable to fetch weather: {}", e));
    println!("Hello, excellent world on this beautiful {}! Tomorrow's weather forecast: {}.", datetime, weather);
}
