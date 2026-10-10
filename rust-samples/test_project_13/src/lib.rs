//! A native library with one C-ABI export, `cb_add`.
//!
//! Built as a `cdylib` it is a shared library (`libtest_project_13.so` on Linux and
//! Android, `test_project_13.dll` on Windows) that C, C# (P/Invoke) and other languages
//! can load. Built as an `rlib` it is an ordinary Rust library, used by `src/main.rs`.

/// Adds two 32-bit integers, wrapping on overflow.
///
/// Exported unmangled with the C calling convention, so its symbol name is `cb_add`.
#[unsafe(no_mangle)]
pub extern "C" fn cb_add(a: i32, b: i32) -> i32 {
    a.wrapping_add(b)
}

#[cfg(test)]
mod tests {
    use super::cb_add;

    #[test]
    fn adds_two_numbers() {
        assert_eq!(cb_add(2, 3), 5);
    }

    #[test]
    fn wraps_on_overflow() {
        assert_eq!(cb_add(i32::MAX, 1), i32::MIN);
    }
}
