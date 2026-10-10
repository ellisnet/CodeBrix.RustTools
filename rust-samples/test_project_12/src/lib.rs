//! `batchjob`: record generation, JSONL serialization and validation.

use std::collections::HashSet;
use std::io::{BufRead, Write};

use chrono::{DateTime, Duration, TimeZone, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Status {
    Pending,
    Active,
    Archived,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Record {
    pub id: Uuid,
    pub sequence: u64,
    pub created_at: DateTime<Utc>,
    pub name: String,
    pub score: f64,
    pub status: Status,
    pub tags: Vec<String>,
}

/// Small deterministic xorshift generator so record *content* is reproducible
/// (only the v4 UUIDs are random).
#[derive(Debug, Clone)]
pub struct Rng(u64);

impl Rng {
    pub fn new(seed: u64) -> Self {
        Self(seed.max(1))
    }

    pub fn next_u64(&mut self) -> u64 {
        let mut x = self.0;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        self.0 = x;
        x
    }

    pub fn below(&mut self, n: u64) -> u64 {
        self.next_u64() % n
    }
}

const ADJECTIVES: [&str; 6] = ["amber", "brisk", "cobalt", "dusky", "eager", "frosty"];
const NOUNS: [&str; 6] = ["otter", "harbor", "lantern", "meadow", "comet", "spruce"];
const TAGS: [&str; 5] = ["alpha", "beta", "gamma", "delta", "omega"];

pub fn epoch() -> DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 1, 1, 0, 0, 0).single().expect("valid date")
}

pub fn generate_record(sequence: u64, rng: &mut Rng) -> Record {
    let name = format!("{}-{}-{sequence}", ADJECTIVES[rng.below(6) as usize], NOUNS[rng.below(6) as usize]);
    let tag_count = rng.below(3) as usize + 1;
    let tags = (0..tag_count).map(|_| TAGS[rng.below(5) as usize].to_string()).collect();
    Record {
        id: Uuid::new_v4(),
        sequence,
        created_at: epoch() + Duration::seconds(sequence as i64 * 37),
        name,
        score: rng.below(10_000) as f64 / 100.0,
        status: match rng.below(3) {
            0 => Status::Pending,
            1 => Status::Active,
            _ => Status::Archived,
        },
        tags,
    }
}

/// Writes one JSON object per line.
pub fn write_jsonl<W: Write>(mut writer: W, record: &Record) -> std::io::Result<()> {
    serde_json::to_writer(&mut writer, record)?;
    writer.write_all(b"\n")
}

#[derive(Debug, Default, PartialEq)]
pub struct ValidationReport {
    pub records: u64,
    pub duplicate_ids: u64,
    pub out_of_order: u64,
    pub bad_lines: Vec<usize>,
    pub score_sum: f64,
}

impl ValidationReport {
    pub fn is_valid(&self, expected: u64) -> bool {
        self.records == expected && self.duplicate_ids == 0 && self.out_of_order == 0 && self.bad_lines.is_empty()
    }
}

/// Reads JSONL back and checks ids are unique and sequences/timestamps increase.
pub fn validate_jsonl<R: BufRead>(reader: R, mut on_line: impl FnMut()) -> std::io::Result<ValidationReport> {
    let mut report = ValidationReport::default();
    let mut seen = HashSet::new();
    let mut last: Option<(u64, DateTime<Utc>)> = None;
    for (index, line) in reader.lines().enumerate() {
        let line = line?;
        on_line();
        match serde_json::from_str::<Record>(&line) {
            Ok(record) => {
                report.records += 1;
                report.score_sum += record.score;
                if !seen.insert(record.id) {
                    report.duplicate_ids += 1;
                }
                if let Some((seq, at)) = last {
                    if record.sequence <= seq || record.created_at <= at {
                        report.out_of_order += 1;
                    }
                }
                last = Some((record.sequence, record.created_at));
            }
            Err(e) => {
                log::warn!("line {}: {e}", index + 1);
                report.bad_lines.push(index + 1);
            }
        }
    }
    Ok(report)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn records(n: u64) -> Vec<Record> {
        let mut rng = Rng::new(42);
        (0..n).map(|i| generate_record(i, &mut rng)).collect()
    }

    #[test]
    fn generation_is_deterministic_except_ids() {
        let (a, b) = (records(3), records(3));
        assert_eq!(a[2].name, b[2].name);
        assert_eq!(a[2].score, b[2].score);
        assert_ne!(a[2].id, b[2].id);
        assert_eq!(a[0].id.get_version_num(), 4);
    }

    #[test]
    fn jsonl_round_trip_validates() {
        let mut buf = Vec::new();
        for r in records(50) {
            write_jsonl(&mut buf, &r).unwrap();
        }
        let mut lines = 0;
        let report = validate_jsonl(buf.as_slice(), || lines += 1).unwrap();
        assert_eq!(lines, 50);
        assert!(report.is_valid(50), "{report:?}");
    }

    #[test]
    fn detects_duplicates_and_bad_lines() {
        let rs = records(2);
        let mut buf = Vec::new();
        write_jsonl(&mut buf, &rs[0]).unwrap();
        write_jsonl(&mut buf, &rs[0]).unwrap();
        buf.extend_from_slice(b"{not json}\n");
        let report = validate_jsonl(buf.as_slice(), || {}).unwrap();
        assert_eq!(report.records, 2);
        assert_eq!(report.duplicate_ids, 1);
        assert_eq!(report.out_of_order, 1);
        assert_eq!(report.bad_lines, vec![3]);
    }

    #[test]
    fn status_serializes_snake_case() {
        assert_eq!(serde_json::to_string(&Status::Archived).unwrap(), "\"archived\"");
    }
}
