//! test_project_07: parallel word-frequency counting.
//!
//! Reads `data/*.txt` (by default the bundled Moby-Dick excerpt), tokenizes the paragraphs
//! in parallel with rayon, and writes results to `output/` inside this sample's folder.
//!
//! Usage: test_project_07 [--input FILE] [--out-dir DIR] [--top N]

use std::collections::{HashMap, HashSet};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::ExitCode;
use std::sync::LazyLock;

use rayon::prelude::*;
use regex::Regex;

/// Words: runs of Unicode letters, optionally joined by straight or curly apostrophes
/// or single hyphens (e.g. `people's`, `water-gazers`).
static WORD_RE: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"\p{L}+(?:['\u{2019}-]\p{L}+)*").expect("valid regex"));

/// Capitalized words that do not start a sentence - a crude proper-noun detector.
static PROPER_NOUN_RE: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"[a-z,;]\s+([A-Z][a-z]{2,})").expect("valid regex"));

static STOP_WORDS: LazyLock<HashSet<&'static str>> = LazyLock::new(|| {
    "a an and are as at be but by do for from he her his i if in is it its me my no not of on or so some that the their them then there these they this to up was we what when with you your all will"
        .split_whitespace()
        .collect()
});

pub type Counts = HashMap<String, usize>;

/// Counts words in one chunk of text (lower-cased, apostrophes normalized).
pub fn count_words(text: &str, skip_stop_words: bool) -> Counts {
    let mut counts = Counts::new();
    for m in WORD_RE.find_iter(text) {
        let word = m.as_str().to_lowercase().replace('\u{2019}', "'");
        if skip_stop_words && STOP_WORDS.contains(word.as_str()) {
            continue;
        }
        *counts.entry(word).or_insert(0) += 1;
    }
    counts
}

fn merge(mut a: Counts, b: Counts) -> Counts {
    for (word, n) in b {
        *a.entry(word).or_insert(0) += n;
    }
    a
}

/// Splits on blank lines and counts every paragraph in parallel, then reduces.
pub fn parallel_counts(text: &str, skip_stop_words: bool) -> Counts {
    let paragraphs: Vec<&str> = text.split("\n\n").filter(|p| !p.trim().is_empty()).collect();
    paragraphs
        .par_iter()
        .map(|p| count_words(p, skip_stop_words))
        .reduce(Counts::new, merge)
}

/// Sorted by descending count, then alphabetically.
pub fn top_n(counts: &Counts, n: usize) -> Vec<(String, usize)> {
    let mut entries: Vec<(String, usize)> = counts.iter().map(|(w, c)| (w.clone(), *c)).collect();
    entries.par_sort_unstable_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));
    entries.truncate(n);
    entries
}

pub fn proper_nouns(text: &str) -> Vec<String> {
    let mut found: Vec<String> = PROPER_NOUN_RE.captures_iter(text).map(|c| c[1].to_string()).collect();
    found.sort();
    found.dedup();
    found
}

struct Options {
    input: PathBuf,
    out_dir: PathBuf,
    top: usize,
}

fn parse_args() -> Result<Options, String> {
    let sample_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    let mut opts = Options {
        input: sample_dir.join("data").join("moby_dick_loomings.txt"),
        out_dir: sample_dir.join("output"),
        top: 25,
    };
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        let mut value = || args.next().ok_or_else(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--input" => opts.input = PathBuf::from(value()?),
            "--out-dir" => opts.out_dir = PathBuf::from(value()?),
            "--top" => opts.top = value()?.parse().map_err(|_| "--top needs a number".to_string())?,
            "-h" | "--help" => return Err("usage: test_project_07 [--input FILE] [--out-dir DIR] [--top N]".into()),
            other => return Err(format!("unknown argument {other}")),
        }
    }
    Ok(opts)
}

fn run(opts: &Options) -> std::io::Result<()> {
    let text = fs::read_to_string(&opts.input)?;
    let all = parallel_counts(&text, false);
    let content = parallel_counts(&text, true);
    let total: usize = all.values().sum();

    fs::create_dir_all(&opts.out_dir)?;
    let csv_path = opts.out_dir.join("word_frequencies.csv");
    let mut csv = fs::File::create(&csv_path)?;
    writeln!(csv, "word,count")?;
    for (word, count) in top_n(&all, usize::MAX) {
        writeln!(csv, "{word},{count}")?;
    }

    let report_path = opts.out_dir.join("report.txt");
    let mut report = fs::File::create(&report_path)?;
    writeln!(report, "Source: {}", opts.input.file_name().unwrap_or_default().to_string_lossy())?;
    writeln!(report, "Rayon threads: {}", rayon::current_num_threads())?;
    writeln!(report, "Total words: {total}, distinct: {}", all.len())?;
    writeln!(report, "Proper nouns: {}", proper_nouns(&text).join(", "))?;
    writeln!(report, "\nTop {} words (stop words removed):", opts.top)?;
    for (rank, (word, count)) in top_n(&content, opts.top).iter().enumerate() {
        writeln!(report, "{:>3}. {word:<16} {count}", rank + 1)?;
    }

    // Read-only use of `dirs`: the home directory is only printed, never written to.
    match dirs::home_dir() {
        Some(home) => println!("home directory (informational only, nothing is written there): {}", home.display()),
        None => println!("home directory: unknown"),
    }
    println!("{total} words ({} distinct) using {} threads", all.len(), rayon::current_num_threads());
    println!("wrote {}", csv_path.display());
    println!("wrote {}", report_path.display());
    Ok(())
}

fn main() -> ExitCode {
    let opts = match parse_args() {
        Ok(opts) => opts,
        Err(msg) => {
            eprintln!("{msg}");
            return ExitCode::from(2);
        }
    };
    match run(&opts) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tokenizes_apostrophes_and_hyphens() {
        let counts = count_words("The water-gazers’ crowd; people\u{2019}s hats. THE end", false);
        assert_eq!(counts["the"], 2);
        assert_eq!(counts["water-gazers"], 1);
        assert_eq!(counts["people's"], 1);
    }

    #[test]
    fn stop_words_are_skipped() {
        let counts = count_words("the sea and the ship", true);
        assert_eq!(counts.len(), 2);
        assert!(!counts.contains_key("the"));
    }

    #[test]
    fn parallel_equals_sequential() {
        let text = "one two two\n\nthree three three\n\none\n";
        assert_eq!(parallel_counts(text, false), count_words(text, false));
    }

    #[test]
    fn top_n_orders_by_count_then_word() {
        let counts = count_words("b a c b a b", false);
        assert_eq!(top_n(&counts, 2), vec![("b".to_string(), 3), ("a".to_string(), 2)]);
    }

    #[test]
    fn finds_proper_nouns() {
        assert_eq!(proper_nouns("walk from Corlears Hook to Coenties Slip, by Whitehall"), vec!["Coenties", "Corlears", "Whitehall"]);
    }

    #[test]
    fn bundled_fixture_has_text() {
        let text = fs::read_to_string(Path::new(env!("CARGO_MANIFEST_DIR")).join("data/moby_dick_loomings.txt")).unwrap();
        assert!(parallel_counts(&text, false)["water"] >= 5);
    }
}
