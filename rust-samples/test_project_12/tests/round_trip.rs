//! Integration test against the library's public API (in memory, no files).

use batchjob::{Rng, generate_record, validate_jsonl, write_jsonl};

#[test]
fn thousand_records_round_trip() {
    let mut rng = Rng::new(9);
    let mut buf = Vec::new();
    for i in 0..1000 {
        write_jsonl(&mut buf, &generate_record(i, &mut rng)).unwrap();
    }
    assert_eq!(buf.iter().filter(|&&b| b == b'\n').count(), 1000);
    let report = validate_jsonl(buf.as_slice(), || {}).unwrap();
    assert!(report.is_valid(1000));
    assert!(report.score_sum > 0.0);
}
