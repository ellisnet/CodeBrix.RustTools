//! JSON reports (optional feature `json`).

use crate::TextStats;

/// Serializes text statistics as pretty-printed JSON.
pub fn stats_to_json(stats: &TextStats) -> String {
    serde_json::to_string_pretty(stats).expect("TextStats always serializes")
}

#[cfg(test)]
mod tests {
    #[test]
    fn produces_json_object() {
        let json = super::stats_to_json(&crate::stats("one two"));
        let value: serde_json::Value = serde_json::from_str(&json).unwrap();
        assert_eq!(value["words"], 2);
    }
}
