//! gamma: a standalone crate used by alpha through a relative path.

/// Returns `n * 2` (wrapping).
pub fn double(n: u32) -> u32 {
    n.wrapping_mul(2)
}
