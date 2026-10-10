//! Fixture crate: `license = "Proprietary-Acme-1.0"` (not an SPDX identifier).

/// Reverses the order of the words in `text`.
pub fn reverse_words(text: &str) -> String {
    text.split_whitespace().rev().collect::<Vec<_>>().join(" ")
}
