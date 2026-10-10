//! Integration test for the library's public API, as seen by another crate.

use unitconv_core::{ALL_UNITS, Quantity, Unit};

#[test]
fn every_unit_converts_to_itself() {
    for unit in ALL_UNITS {
        let q = Quantity::new(42.0, unit).convert_to(unit).unwrap();
        assert!((q.value - 42.0).abs() < 1e-9, "{unit} did not round-trip");
    }
}

#[test]
fn quantity_serializes_to_json() {
    let json = serde_json::to_string(&Quantity::new(1.5, Unit::Pound)).unwrap();
    assert_eq!(json, r#"{"value":1.5,"unit":"Pound"}"#);
}
