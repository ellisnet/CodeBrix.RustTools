//! alpha: combines beta (a workspace member) and gamma (a path dependency that is not a
//! member).

/// Adds one through beta and doubles through gamma: `(n + 1) * 2`.
pub fn transform(n: u32) -> u32 {
    gamma::double(beta::increment(n))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn transform_adds_then_doubles() {
        assert_eq!(transform(4), 10);
    }
}
