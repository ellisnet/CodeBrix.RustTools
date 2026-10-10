//! Integration tests: compiled as a separate crate that links against `textkit`.

#[test]
fn stats_on_multiline_text() {
    let s = textkit::stats("one two three.\nfour five!\n");
    assert_eq!(s.words, 5);
    assert_eq!(s.lines, 2);
    assert_eq!(s.sentences, 2);
}

#[test]
fn feature_list_matches_cfg() {
    let features = textkit::enabled_features();
    assert_eq!(features.contains(&"case"), cfg!(feature = "case"));
    assert_eq!(features.contains(&"json"), cfg!(feature = "json"));
}

#[cfg(feature = "case")]
#[test]
fn case_module_available_by_default() {
    assert_eq!(textkit::case::to_kebab_case("IntegrationTest"), "integration-test");
}

#[cfg(feature = "json")]
#[test]
fn json_module_round_trips() {
    let json = textkit::json::stats_to_json(&textkit::stats("a b c"));
    assert!(json.contains("\"words\": 3"));
}
