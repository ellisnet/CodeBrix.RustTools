//! test_project_11: reads `config.toml` from this sample's folder and renders three PNGs
//! (gradient, Mandelbrot, plasma) into the configured output folder (default `output/`).
//!
//! Usage: test_project_11 [CONFIG_FILE]

mod config;
mod render;

use std::path::{Component, Path, PathBuf};
use std::process::ExitCode;
use std::time::Instant;

use config::{Config, parse_hex_color};

fn run(config_path: &Path) -> Result<(), Box<dyn std::error::Error>> {
    let sample_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    let text = std::fs::read_to_string(config_path).map_err(|e| format!("cannot read {}: {e}", config_path.display()))?;
    let config = Config::parse(&text)?;

    // Keep all output inside this sample's folder: reject absolute paths and `..`.
    let relative = Path::new(&config.output_dir);
    if relative.is_absolute() || relative.components().any(|c| c == Component::ParentDir) {
        return Err(format!("output_dir '{}' must be a relative path inside the sample folder", config.output_dir).into());
    }
    let out_dir = sample_dir.join(relative);
    std::fs::create_dir_all(&out_dir)?;

    let start = parse_hex_color(&config.colors.start)?;
    let end = parse_hex_color(&config.colors.end)?;
    let palette = config.colors.palette.iter().map(|c| parse_hex_color(c)).collect::<Result<Vec<_>, _>>()?;
    let (w, h) = (config.width, config.height);
    let m = &config.mandelbrot;

    let jobs: [(&str, Box<dyn Fn() -> image::RgbImage>); 3] = [
        ("gradient.png", Box::new(|| render::gradient(w, h, start, end))),
        ("mandelbrot.png", Box::new(|| render::mandelbrot(w, h, (m.center_x, m.center_y), m.zoom, m.max_iterations, &palette))),
        ("plasma.png", Box::new(|| render::plasma(w, h, config.plasma.scale, config.plasma.seed, &palette))),
    ];
    for (name, job) in jobs {
        let timer = Instant::now();
        let image = job();
        let path = out_dir.join(name);
        image.save(&path)?;
        println!("{w}x{h} {name:<15} {:>6} ms -> {}", timer.elapsed().as_millis(), path.display());
    }
    Ok(())
}

fn main() -> ExitCode {
    let config_path = std::env::args_os()
        .nth(1)
        .map(PathBuf::from)
        .unwrap_or_else(|| Path::new(env!("CARGO_MANIFEST_DIR")).join("config.toml"));
    match run(&config_path) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}
