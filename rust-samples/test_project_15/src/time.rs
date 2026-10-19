//! Time in ticks.

/// The tick rate: ticks per second = 254016000000.
pub const TICKS_PER_SECOND: i64 = 254_016_000_000;

/// A point in time, in ticks (ticks per second = 254016000000, see [`TICKS_PER_SECOND`]).
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash, Default)]
pub struct Tick(pub i64);

impl Tick {
    /// The tick nearest to `seconds`.
    pub fn from_seconds_f64(seconds: f64) -> Tick {
        Tick((seconds * TICKS_PER_SECOND as f64).round() as i64)
    }

    /// This tick in seconds.
    pub fn seconds(&self) -> f64 {
        self.0 as f64 / TICKS_PER_SECOND as f64
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tick_round_trip() {
        assert_eq!(Tick::from_seconds_f64(1.5).0, 381_024_000_000);
        assert_eq!(Tick(TICKS_PER_SECOND).seconds(), 1.0);
    }
}
