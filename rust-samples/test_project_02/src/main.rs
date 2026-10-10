//! test_project_02: a small `clap` (derive) CLI with subcommands that reads a
//! CSV fixture and writes a JSON summary into this sample's own `output/` folder.

use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result, bail};
use clap::{Parser, Subcommand};
use serde::{Deserialize, Serialize};

/// Root of this sample; every file it reads or writes lives under here.
const SAMPLE_DIR: &str = env!("CARGO_MANIFEST_DIR");

#[derive(Parser, Debug)]
#[command(name = "salesctl", version, about = "Summarize a sales CSV file")]
struct Cli {
    /// CSV input file (defaults to the bundled data/sales.csv fixture)
    #[arg(short, long, global = true)]
    input: Option<PathBuf>,

    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    /// Print every row of the CSV file
    List {
        /// Only show rows for this region
        #[arg(short, long)]
        region: Option<String>,
    },
    /// Compute per-region and per-product totals and write them as JSON
    Summarize {
        /// Output folder (defaults to <sample>/output)
        #[arg(short, long)]
        out_dir: Option<PathBuf>,
        /// Write compact (single-line) JSON instead of pretty-printed JSON
        #[arg(long)]
        compact: bool,
    },
}

#[derive(Debug, Clone, Deserialize, PartialEq)]
struct SaleRecord {
    date: String,
    region: String,
    product: String,
    units: u32,
    unit_price: f64,
}

impl SaleRecord {
    fn revenue(&self) -> f64 {
        f64::from(self.units) * self.unit_price
    }
}

#[derive(Debug, Default, Serialize, PartialEq)]
struct Totals {
    units: u32,
    revenue: f64,
}

#[derive(Debug, Serialize)]
struct Summary {
    record_count: usize,
    total_units: u32,
    total_revenue: f64,
    by_region: BTreeMap<String, Totals>,
    by_product: BTreeMap<String, Totals>,
}

fn read_records<R: std::io::Read>(reader: R) -> Result<Vec<SaleRecord>> {
    let mut csv_reader = csv::Reader::from_reader(reader);
    let mut records = Vec::new();
    for (index, row) in csv_reader.deserialize().enumerate() {
        let record: SaleRecord = row.with_context(|| format!("bad CSV row {}", index + 2))?;
        records.push(record);
    }
    Ok(records)
}

fn round2(value: f64) -> f64 {
    (value * 100.0).round() / 100.0
}

fn summarize(records: &[SaleRecord]) -> Summary {
    let mut by_region: BTreeMap<String, Totals> = BTreeMap::new();
    let mut by_product: BTreeMap<String, Totals> = BTreeMap::new();
    for record in records {
        for (map, key) in [(&mut by_region, &record.region), (&mut by_product, &record.product)] {
            let entry = map.entry(key.clone()).or_default();
            entry.units += record.units;
            entry.revenue = round2(entry.revenue + record.revenue());
        }
    }
    Summary {
        record_count: records.len(),
        total_units: records.iter().map(|r| r.units).sum(),
        total_revenue: round2(records.iter().map(SaleRecord::revenue).sum()),
        by_region,
        by_product,
    }
}

fn main() -> Result<()> {
    let cli = Cli::parse();
    let sample_dir = Path::new(SAMPLE_DIR);
    let input = cli.input.unwrap_or_else(|| sample_dir.join("data").join("sales.csv"));
    let file = fs::File::open(&input).with_context(|| format!("cannot open {}", input.display()))?;
    let records = read_records(file)?;
    if records.is_empty() {
        bail!("{} contains no records", input.display());
    }

    match cli.command {
        Command::List { region } => {
            for r in records.iter().filter(|r| region.as_ref().is_none_or(|want| want.eq_ignore_ascii_case(&r.region))) {
                println!("{}  {:<6} {:<7} {:>3} x {:>6.2} = {:>8.2}", r.date, r.region, r.product, r.units, r.unit_price, r.revenue());
            }
        }
        Command::Summarize { out_dir, compact } => {
            let out_dir = out_dir.unwrap_or_else(|| sample_dir.join("output"));
            fs::create_dir_all(&out_dir).with_context(|| format!("cannot create {}", out_dir.display()))?;
            let summary = summarize(&records);
            let json = if compact { serde_json::to_string(&summary)? } else { serde_json::to_string_pretty(&summary)? };
            let out_file = out_dir.join("summary.json");
            fs::write(&out_file, json + "\n").with_context(|| format!("cannot write {}", out_file.display()))?;
            println!("Summarized {} records ({} units, revenue {:.2}) -> {}", summary.record_count, summary.total_units, summary.total_revenue, out_file.display());
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use clap::CommandFactory;

    const SAMPLE: &str = "date,region,product,units,unit_price\n2026-01-01,North,Widget,2,1.50\n2026-01-02,South,Widget,3,1.50\n2026-01-02,North,Gizmo,1,10.00\n";

    #[test]
    fn cli_definition_is_valid() {
        Cli::command().debug_assert();
    }

    #[test]
    fn parses_records() {
        let records = read_records(SAMPLE.as_bytes()).unwrap();
        assert_eq!(records.len(), 3);
        assert_eq!(records[2].product, "Gizmo");
    }

    #[test]
    fn summarizes_totals() {
        let summary = summarize(&read_records(SAMPLE.as_bytes()).unwrap());
        assert_eq!(summary.total_units, 6);
        assert_eq!(summary.total_revenue, 17.5);
        assert_eq!(summary.by_region["North"], Totals { units: 3, revenue: 13.0 });
        assert_eq!(summary.by_product["Widget"].units, 5);
    }

    #[test]
    fn rejects_bad_rows() {
        let bad = "date,region,product,units,unit_price\n2026-01-01,North,Widget,lots,1.0\n";
        assert!(read_records(bad.as_bytes()).is_err());
    }

    #[test]
    fn bundled_fixture_parses() {
        let path = Path::new(SAMPLE_DIR).join("data").join("sales.csv");
        let records = read_records(fs::File::open(path).unwrap()).unwrap();
        assert_eq!(records.len(), 12);
    }
}
