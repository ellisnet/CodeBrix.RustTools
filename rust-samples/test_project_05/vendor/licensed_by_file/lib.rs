//! Fixture crate: licensed only via `license-file`.

/// Counts the vowels in `text` (ASCII, case-insensitive).
pub fn vowel_count(text: &str) -> usize {
    text.chars().filter(|c| "aeiouAEIOU".contains(*c)).count()
}
