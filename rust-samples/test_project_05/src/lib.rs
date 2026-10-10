//! `textkit`: small text utilities, used by the `textkit` binary, the examples and the
//! integration tests in this package.
//!
//! Feature flags:
//! * `case` (default) - enables the [`case`] module.
//! * `json` (optional) - enables the [`json`] module and the `serde` / `serde_json` dependencies.

#[cfg(feature = "case")]
pub mod case;

#[cfg(feature = "json")]
pub mod json;

/// Basic statistics about a piece of text.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
#[cfg_attr(feature = "json", derive(serde::Serialize))]
pub struct TextStats {
    pub chars: usize,
    pub words: usize,
    pub lines: usize,
    pub sentences: usize,
    pub longest_word: String,
}

/// Computes [`TextStats`] for `text`.
pub fn stats(text: &str) -> TextStats {
    let words: Vec<&str> = text
        .split_whitespace()
        .map(|w| w.trim_matches(|c: char| !c.is_alphanumeric()))
        .filter(|w| !w.is_empty())
        .collect();
    let longest_word = words.iter().fold("", |best, w| if w.chars().count() > best.chars().count() { w } else { best });
    TextStats {
        chars: text.chars().count(),
        words: words.len(),
        lines: text.lines().count(),
        sentences: text.matches(['.', '!', '?']).count(),
        longest_word: longest_word.to_string(),
    }
}

/// Returns true when `text` reads the same forwards and backwards, ignoring case,
/// whitespace and punctuation.
///
/// ```
/// assert!(textkit::is_palindrome("Never odd or even"));
/// assert!(!textkit::is_palindrome("Cargo"));
/// ```
pub fn is_palindrome(text: &str) -> bool {
    let cleaned: Vec<char> = text.chars().filter(|c| c.is_alphanumeric()).flat_map(char::to_lowercase).collect();
    !cleaned.is_empty() && cleaned.iter().eq(cleaned.iter().rev())
}

/// Results of the helpers provided by the three local `vendor/` crates.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VendorInfo {
    /// From `licensed_by_file`.
    pub vowels: usize,
    /// From `unknown_license_id`.
    pub reversed: String,
    /// From `no_license_at_all`.
    pub pangram: bool,
}

/// Runs `text` through the vendored path-dependency crates.
pub fn vendor_info(text: &str) -> VendorInfo {
    VendorInfo {
        vowels: licensed_by_file::vowel_count(text),
        reversed: unknown_license_id::reverse_words(text),
        pangram: no_license_at_all::is_pangram(text),
    }
}

/// Lists the feature flags this build of the library was compiled with.
pub fn enabled_features() -> Vec<&'static str> {
    let mut features = Vec::new();
    if cfg!(feature = "case") {
        features.push("case");
    }
    if cfg!(feature = "json") {
        features.push("json");
    }
    features
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn counts_words_and_sentences() {
        let s = stats("Hello, world! This is textkit.\nSecond line?");
        assert_eq!(s.words, 7);
        assert_eq!(s.sentences, 3);
        assert_eq!(s.lines, 2);
        assert_eq!(s.longest_word, "textkit");
    }

    #[test]
    fn vendor_crates_are_used() {
        let info = vendor_info("The quick brown fox jumps over the lazy dog");
        assert_eq!(info.vowels, 11);
        assert_eq!(info.reversed, "dog lazy the over jumps fox brown quick The");
        assert!(info.pangram);
    }

    #[test]
    fn palindromes() {
        assert!(is_palindrome("A man, a plan, a canal: Panama"));
        assert!(!is_palindrome("textkit"));
        assert!(!is_palindrome("!!!"));
    }
}
