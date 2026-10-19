//! beta: a workspace member library.

/// Returns `n + 1` (wrapping).
pub fn increment(n: u32) -> u32 {
    n.wrapping_add(1)
}
