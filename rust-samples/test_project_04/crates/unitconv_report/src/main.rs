//! Writes a JSON conversion table for every pair of compatible units into
//! `<workspace root>/output/conversion_table.json`.

use std::fs;
use std::path::Path;

use anyhow::{Context, Result};
use serde::Serialize;
use unitconv_core::{ALL_UNITS, Quantity};

#[derive(Serialize)]
struct Row {
    from: Quantity,
    to: Quantity,
}

fn build_table() -> Vec<Row> {
    let mut rows = Vec::new();
    for from in ALL_UNITS {
        for to in ALL_UNITS {
            if from != to {
                if let Ok(converted) = Quantity::new(1.0, from).convert_to(to) {
                    rows.push(Row { from: Quantity::new(1.0, from), to: converted });
                }
            }
        }
    }
    rows
}

fn main() -> Result<()> {
    // CARGO_MANIFEST_DIR is this member crate's folder; the workspace root is two levels up.
    let workspace_root = Path::new(env!("CARGO_MANIFEST_DIR")).join("..").join("..");
    let out_dir = workspace_root.join("output");
    fs::create_dir_all(&out_dir).with_context(|| format!("creating {}", out_dir.display()))?;
    let table = build_table();
    let out_file = out_dir.join("conversion_table.json");
    fs::write(&out_file, serde_json::to_string_pretty(&table)?).with_context(|| format!("writing {}", out_file.display()))?;
    println!("Wrote {} conversions to {}", table.len(), out_file.canonicalize()?.display());
    Ok(())
}

#[cfg(test)]
mod tests {
    #[test]
    fn table_only_has_compatible_pairs() {
        // 4 length units -> 12 pairs, 3 mass -> 6, 3 temperature -> 6.
        assert_eq!(super::build_table().len(), 24);
    }
}
