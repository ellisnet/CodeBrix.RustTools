//! cargo run --example single_record
//! Prints one generated record as pretty JSON (writes no files).
fn main() {
    let mut rng = batchjob::Rng::new(1);
    let record = batchjob::generate_record(0, &mut rng);
    println!("{}", serde_json::to_string_pretty(&record).expect("serializable"));
}
