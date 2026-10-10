//! cargo bench   (criterion; `harness = false` in Cargo.toml)

use std::hint::black_box;

use batchjob::{Rng, generate_record, validate_jsonl, write_jsonl};
use criterion::{Criterion, Throughput, criterion_group, criterion_main};

fn bench_generate(c: &mut Criterion) {
    let mut rng = Rng::new(7);
    c.bench_function("generate_record", |b| b.iter(|| generate_record(black_box(123), &mut rng)));
}

fn bench_serialize_and_validate(c: &mut Criterion) {
    let mut rng = Rng::new(7);
    let records: Vec<_> = (0..1000).map(|i| generate_record(i, &mut rng)).collect();
    let mut jsonl = Vec::new();
    for r in &records {
        write_jsonl(&mut jsonl, r).unwrap();
    }

    let mut group = c.benchmark_group("jsonl_1000");
    group.throughput(Throughput::Elements(records.len() as u64));
    group.bench_function("write", |b| {
        b.iter(|| {
            let mut buf = Vec::with_capacity(jsonl.len());
            for r in &records {
                write_jsonl(&mut buf, black_box(r)).unwrap();
            }
            buf
        })
    });
    group.bench_function("validate", |b| b.iter(|| validate_jsonl(black_box(jsonl.as_slice()), || {}).unwrap()));
    group.finish();
}

criterion_group!(benches, bench_generate, bench_serialize_and_validate);
criterion_main!(benches);
