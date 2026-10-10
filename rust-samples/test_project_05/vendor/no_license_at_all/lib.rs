//! Fixture crate: no `license` and no `license-file`.

/// Returns true if `text` contains every letter a-z at least once.
pub fn is_pangram(text: &str) -> bool {
    let lower = text.to_ascii_lowercase();
    ('a'..='z').all(|c| lower.contains(c))
}
