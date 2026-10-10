//! Domain logic shared by the workspace binaries: units, quantities and conversion.

use std::fmt;
use std::str::FromStr;

use serde::{Deserialize, Serialize};
use thiserror::Error;

/// The dimension a unit measures; only units of the same dimension convert.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Dimension {
    Length,
    Mass,
    Temperature,
}

/// Supported units.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Unit {
    Meter,
    Kilometer,
    Foot,
    Mile,
    Gram,
    Kilogram,
    Pound,
    Celsius,
    Fahrenheit,
    Kelvin,
}

pub const ALL_UNITS: [Unit; 10] = [
    Unit::Meter,
    Unit::Kilometer,
    Unit::Foot,
    Unit::Mile,
    Unit::Gram,
    Unit::Kilogram,
    Unit::Pound,
    Unit::Celsius,
    Unit::Fahrenheit,
    Unit::Kelvin,
];

#[derive(Debug, Error, PartialEq)]
pub enum ConvertError {
    #[error("unknown unit symbol '{0}'")]
    UnknownUnit(String),
    #[error("cannot convert {from:?} to {to:?}: different dimensions")]
    DimensionMismatch { from: Dimension, to: Dimension },
    #[error("'{0}' is not a number")]
    BadNumber(String),
}

impl Unit {
    pub fn symbol(self) -> &'static str {
        match self {
            Unit::Meter => "m",
            Unit::Kilometer => "km",
            Unit::Foot => "ft",
            Unit::Mile => "mi",
            Unit::Gram => "g",
            Unit::Kilogram => "kg",
            Unit::Pound => "lb",
            Unit::Celsius => "C",
            Unit::Fahrenheit => "F",
            Unit::Kelvin => "K",
        }
    }

    pub fn dimension(self) -> Dimension {
        match self {
            Unit::Meter | Unit::Kilometer | Unit::Foot | Unit::Mile => Dimension::Length,
            Unit::Gram | Unit::Kilogram | Unit::Pound => Dimension::Mass,
            Unit::Celsius | Unit::Fahrenheit | Unit::Kelvin => Dimension::Temperature,
        }
    }

    /// Converts a value in this unit to the dimension's base unit (m, g, K).
    fn to_base(self, v: f64) -> f64 {
        match self {
            Unit::Meter | Unit::Gram | Unit::Kelvin => v,
            Unit::Kilometer | Unit::Kilogram => v * 1000.0,
            Unit::Foot => v * 0.3048,
            Unit::Mile => v * 1609.344,
            Unit::Pound => v * 453.592_37,
            Unit::Celsius => v + 273.15,
            Unit::Fahrenheit => (v - 32.0) * 5.0 / 9.0 + 273.15,
        }
    }

    fn from_base(self, v: f64) -> f64 {
        match self {
            Unit::Meter | Unit::Gram | Unit::Kelvin => v,
            Unit::Kilometer | Unit::Kilogram => v / 1000.0,
            Unit::Foot => v / 0.3048,
            Unit::Mile => v / 1609.344,
            Unit::Pound => v / 453.592_37,
            Unit::Celsius => v - 273.15,
            Unit::Fahrenheit => (v - 273.15) * 9.0 / 5.0 + 32.0,
        }
    }
}

impl fmt::Display for Unit {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.symbol())
    }
}

impl FromStr for Unit {
    type Err = ConvertError;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        ALL_UNITS
            .iter()
            .copied()
            .find(|u| u.symbol().eq_ignore_ascii_case(s))
            .ok_or_else(|| ConvertError::UnknownUnit(s.to_string()))
    }
}

/// A value with a unit.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Quantity {
    pub value: f64,
    pub unit: Unit,
}

impl Quantity {
    pub fn new(value: f64, unit: Unit) -> Self {
        Self { value, unit }
    }

    pub fn convert_to(self, target: Unit) -> Result<Quantity, ConvertError> {
        if self.unit.dimension() != target.dimension() {
            return Err(ConvertError::DimensionMismatch { from: self.unit.dimension(), to: target.dimension() });
        }
        Ok(Quantity::new(target.from_base(self.unit.to_base(self.value)), target))
    }
}

impl fmt::Display for Quantity {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{:.4} {}", self.value, self.unit)
    }
}

impl FromStr for Quantity {
    type Err = ConvertError;

    /// Parses strings such as `"12.5 km"` or `"98.6F"`.
    fn from_str(s: &str) -> Result<Self, Self::Err> {
        let s = s.trim();
        let split = s
            .find(|c: char| c.is_ascii_alphabetic())
            .ok_or_else(|| ConvertError::UnknownUnit(String::new()))?;
        let (number, unit) = s.split_at(split);
        let value = number.trim().parse::<f64>().map_err(|_| ConvertError::BadNumber(number.trim().to_string()))?;
        Ok(Quantity::new(value, unit.trim().parse()?))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn close(a: f64, b: f64) -> bool {
        (a - b).abs() < 1e-9
    }

    #[test]
    fn length_round_trip() {
        let q = Quantity::new(1.0, Unit::Mile).convert_to(Unit::Kilometer).unwrap();
        assert!(close(q.value, 1.609344));
        let back = q.convert_to(Unit::Mile).unwrap();
        assert!(close(back.value, 1.0));
    }

    #[test]
    fn temperatures() {
        let f = Quantity::new(100.0, Unit::Celsius).convert_to(Unit::Fahrenheit).unwrap();
        assert!(close(f.value, 212.0));
        let k = Quantity::new(-40.0, Unit::Fahrenheit).convert_to(Unit::Kelvin).unwrap();
        assert!(close(k.value, 233.15));
    }

    #[test]
    fn mismatch_is_an_error() {
        let err = Quantity::new(1.0, Unit::Gram).convert_to(Unit::Meter).unwrap_err();
        assert_eq!(err, ConvertError::DimensionMismatch { from: Dimension::Mass, to: Dimension::Length });
    }

    #[test]
    fn parses_quantities() {
        assert_eq!("12.5 km".parse::<Quantity>().unwrap(), Quantity::new(12.5, Unit::Kilometer));
        assert_eq!("98.6F".parse::<Quantity>().unwrap(), Quantity::new(98.6, Unit::Fahrenheit));
        assert!(matches!("abc".parse::<Quantity>(), Err(ConvertError::BadNumber(_))));
        assert!(matches!("3 parsecs".parse::<Quantity>(), Err(ConvertError::UnknownUnit(_))));
    }
}
