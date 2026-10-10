//! Batch job: generates N records, writes `output/records.jsonl` (inside this sample's
//! folder) with a progress bar, then reads the file back and validates it.
//!
//! Usage: test_project_12 [--count N] [--seed S]      (RUST_LOG=debug for more logging)

use std::fs::{self, File};
use std::io::{BufReader, BufWriter, Write};
use std::path::Path;
use std::process::ExitCode;
use std::time::Instant;

use batchjob::{Rng, generate_record, validate_jsonl, write_jsonl};
use indicatif::{MultiProgress, ProgressBar, ProgressStyle};
use log::{debug, error, info};

fn parse_args() -> Result<(u64, u64), String> {
    let (mut count, mut seed) = (20_000u64, 2026u64);
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        let value: u64 = args.next().and_then(|v| v.parse().ok()).ok_or_else(|| format!("{arg} needs a number"))?;
        match arg.as_str() {
            "--count" if (1..=5_000_000).contains(&value) => count = value,
            "--count" => return Err("--count must be between 1 and 5000000".into()),
            "--seed" => seed = value,
            other => return Err(format!("unknown argument {other}")),
        }
    }
    Ok((count, seed))
}

fn style(label: &str) -> ProgressStyle {
    ProgressStyle::with_template(&format!("{label:>9} [{{bar:40.cyan/blue}}] {{pos:>7}}/{{len:7}} {{per_sec}} eta {{eta}}"))
        .expect("valid template")
        .progress_chars("=> ")
}

fn run(count: u64, seed: u64) -> std::io::Result<bool> {
    let out_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("output");
    fs::create_dir_all(&out_dir)?;
    let path = out_dir.join("records.jsonl");
    info!("generating {count} records (seed {seed}) into {}", path.display());

    let bars = MultiProgress::new();
    let write_bar = bars.add(ProgressBar::new(count).with_style(style("writing")));
    let read_bar = bars.add(ProgressBar::new(count).with_style(style("reading")));

    let started = Instant::now();
    let mut writer = BufWriter::new(File::create(&path)?);
    let mut rng = Rng::new(seed);
    for sequence in 0..count {
        write_jsonl(&mut writer, &generate_record(sequence, &mut rng))?;
        if sequence % 256 == 0 {
            write_bar.set_position(sequence);
        }
    }
    writer.flush()?;
    write_bar.finish();
    let bytes = fs::metadata(&path)?.len();
    debug!("wrote {bytes} bytes in {:?}", started.elapsed());

    let started = Instant::now();
    let mut lines = 0u64;
    let report = validate_jsonl(BufReader::new(File::open(&path)?), || {
        lines += 1;
        if lines % 256 == 0 {
            read_bar.set_position(lines);
        }
    })?;
    read_bar.set_position(lines);
    read_bar.finish();
    debug!("validated in {:?}", started.elapsed());

    let valid = report.is_valid(count);
    println!(
        "{} records, {:.1} KiB, mean score {:.2}, duplicates {}, out-of-order {}, bad lines {} -> {}",
        report.records,
        bytes as f64 / 1024.0,
        report.score_sum / report.records.max(1) as f64,
        report.duplicate_ids,
        report.out_of_order,
        report.bad_lines.len(),
        if valid { "VALID" } else { "INVALID" }
    );
    Ok(valid)
}

fn main() -> ExitCode {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();
    let (count, seed) = match parse_args() {
        Ok(v) => v,
        Err(msg) => {
            error!("{msg}");
            return ExitCode::from(2);
        }
    };
    match run(count, seed) {
        Ok(true) => ExitCode::SUCCESS,
        Ok(false) => ExitCode::FAILURE,
        Err(e) => {
            error!("I/O error: {e}");
            ExitCode::FAILURE
        }
    }
}
