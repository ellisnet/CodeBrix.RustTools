use serde::Deserialize;

#[derive(Debug, Deserialize, PartialEq)]
pub struct Config {
    #[serde(default = "default_output_dir")]
    pub output_dir: String,
    pub width: u32,
    pub height: u32,
    pub colors: Colors,
    pub mandelbrot: MandelbrotConfig,
    pub plasma: PlasmaConfig,
}

#[derive(Debug, Deserialize, PartialEq)]
pub struct Colors {
    pub start: String,
    pub end: String,
    pub palette: Vec<String>,
}

#[derive(Debug, Deserialize, PartialEq)]
pub struct MandelbrotConfig {
    pub max_iterations: u32,
    pub center_x: f64,
    pub center_y: f64,
    #[serde(default = "one")]
    pub zoom: f64,
}

#[derive(Debug, Deserialize, PartialEq)]
pub struct PlasmaConfig {
    pub scale: f64,
    #[serde(default)]
    pub seed: u32,
}

fn default_output_dir() -> String {
    "output".to_string()
}

fn one() -> f64 {
    1.0
}

#[derive(Debug, PartialEq)]
pub enum ConfigError {
    Toml(String),
    BadColor(String),
    BadSize(u32, u32),
}

impl std::fmt::Display for ConfigError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ConfigError::Toml(msg) => write!(f, "invalid config.toml: {msg}"),
            ConfigError::BadColor(c) => write!(f, "'{c}' is not a #RRGGBB colour"),
            ConfigError::BadSize(w, h) => write!(f, "image size {w}x{h} must be between 1x1 and 4096x4096"),
        }
    }
}

impl std::error::Error for ConfigError {}

impl Config {
    pub fn parse(text: &str) -> Result<Self, ConfigError> {
        let config: Config = toml::from_str(text).map_err(|e| ConfigError::Toml(e.to_string()))?;
        if !(1..=4096).contains(&config.width) || !(1..=4096).contains(&config.height) {
            return Err(ConfigError::BadSize(config.width, config.height));
        }
        for c in [&config.colors.start, &config.colors.end].into_iter().chain(&config.colors.palette) {
            parse_hex_color(c)?;
        }
        if config.colors.palette.len() < 2 {
            return Err(ConfigError::Toml("colors.palette needs at least two entries".into()));
        }
        Ok(config)
    }
}

pub fn parse_hex_color(text: &str) -> Result<[u8; 3], ConfigError> {
    let hex = text.strip_prefix('#').filter(|h| h.len() == 6 && h.chars().all(|c| c.is_ascii_hexdigit()));
    let hex = hex.ok_or_else(|| ConfigError::BadColor(text.to_string()))?;
    let byte = |i: usize| u8::from_str_radix(&hex[i..i + 2], 16).expect("validated hex");
    Ok([byte(0), byte(2), byte(4)])
}

#[cfg(test)]
mod tests {
    use super::*;

    const GOOD: &str = r##"
        width = 10
        height = 5
        [colors]
        start = "#000000"
        end = "#FFFFFF"
        palette = ["#ff0000", "#00ff00"]
        [mandelbrot]
        max_iterations = 50
        center_x = -0.5
        center_y = 0.0
        [plasma]
        scale = 0.1
    "##;

    #[test]
    fn parses_with_defaults() {
        let c = Config::parse(GOOD).unwrap();
        assert_eq!(c.output_dir, "output");
        assert_eq!(c.mandelbrot.zoom, 1.0);
        assert_eq!(c.plasma.seed, 0);
    }

    #[test]
    fn hex_colours() {
        assert_eq!(parse_hex_color("#0A1b2C").unwrap(), [10, 27, 44]);
        assert!(parse_hex_color("0a1b2c").is_err());
        assert!(parse_hex_color("#12345").is_err());
    }

    #[test]
    fn rejects_bad_values() {
        assert!(matches!(Config::parse(&GOOD.replace("#FFFFFF", "white")), Err(ConfigError::BadColor(_))));
        assert!(matches!(Config::parse(&GOOD.replace("width = 10", "width = 0")), Err(ConfigError::BadSize(0, 5))));
        assert!(matches!(Config::parse("width = "), Err(ConfigError::Toml(_))));
    }

    #[test]
    fn bundled_config_is_valid() {
        let text = std::fs::read_to_string(concat!(env!("CARGO_MANIFEST_DIR"), "/config.toml")).unwrap();
        Config::parse(&text).unwrap();
    }
}
