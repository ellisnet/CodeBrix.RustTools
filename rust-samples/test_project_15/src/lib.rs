//! datacrate: a plain Rust library with no C exports. It holds the data-shaped API that
//! CodeBrix.RustTools.CrateBuilder wraps through a generated C facade: Copy structs, a handle
//! type with methods, strings, slices, `Option` / `Result`, a C-like enum and fixed arrays.

use std::fmt;

pub mod shapes;
mod time;

pub use shapes::Rect;
pub use time::{Tick, TICKS_PER_SECOND};

/// A two-dimensional vector of `f64` components.
#[derive(Clone, Copy, Debug, PartialEq, Default)]
#[repr(C)]
pub struct Vec2 {
    /// The horizontal component.
    pub x: f64,
    /// The vertical component.
    pub y: f64,
}

impl Vec2 {
    /// Creates a vector from its two components.
    pub fn new(x: f64, y: f64) -> Vec2 {
        Vec2 { x, y }
    }

    /// The dot product of this vector and `other`.
    pub fn dot(&self, other: Vec2) -> f64 {
        self.x * other.x + self.y * other.y
    }

    /// The Euclidean length of the vector.
    pub fn length(&self) -> f64 {
        self.dot(*self).sqrt()
    }

    /// Linear interpolation from this vector to `to`; `t` = 0 gives this vector, 1 gives `to`.
    pub fn lerp(&self, to: Vec2, t: f64) -> Vec2 {
        Vec2::new(self.x + (to.x - self.x) * t, self.y + (to.y - self.y) * t)
    }
}

/// How an [`Encoder`] processes its input.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(i32)]
pub enum Mode {
    /// Pass the bytes through unchanged.
    A = 1,
    /// Invert every byte.
    B = 2,
}

impl Mode {
    /// Parses a mode name ("a" or "b", any case); `None` for anything else.
    pub fn from_name(name: &str) -> Option<Mode> {
        match name.to_ascii_lowercase().as_str() {
            "a" => Some(Mode::A),
            "b" => Some(Mode::B),
            _ => None,
        }
    }
}

/// The settings an [`Encoder`] is created with.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct EncoderConfig {
    /// The processing mode.
    pub mode: Mode,
    /// Bytes per frame; a frame is counted for every started block of this size.
    pub frame_size: u32,
}

/// A stateful encoder (not `Copy`): it owns a buffer and counts the frames it has encoded.
#[derive(Debug)]
pub struct Encoder {
    config: EncoderConfig,
    frames: u32,
    scratch: Vec<u8>,
}

impl Encoder {
    /// Creates an encoder with the given settings.
    pub fn new(cfg: EncoderConfig) -> Self {
        Encoder { config: cfg, frames: 0, scratch: Vec::new() }
    }

    /// Encodes `input` and returns the encoded bytes.
    pub fn encode(&mut self, input: &[u8]) -> Vec<u8> {
        self.scratch.clear();
        for byte in input {
            self.scratch.push(match self.config.mode {
                Mode::A => *byte,
                Mode::B => !*byte,
            });
        }
        let size = self.config.frame_size.max(1) as usize;
        self.frames += input.len().div_ceil(size) as u32;
        self.scratch.clone()
    }

    /// The number of frames encoded so far.
    pub fn frames(&self) -> u32 {
        self.frames
    }

    #[allow(dead_code)]
    fn reset_internal(&mut self) {
        self.frames = 0;
    }
}

/// Why [`parse_pair`] failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ParseError {
    /// The text has no comma.
    MissingComma,
    /// A side of the comma is not an integer.
    NotANumber,
}

impl fmt::Display for ParseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ParseError::MissingComma => write!(f, "missing comma"),
            ParseError::NotANumber => write!(f, "not a number"),
        }
    }
}

impl std::error::Error for ParseError {}

/// A simple additive checksum of `data` (wrapping).
pub fn checksum(data: &[u8]) -> u32 {
    data.iter().fold(0u32, |acc, b| acc.wrapping_add(*b as u32))
}

/// Returns a greeting for `name`.
pub fn greet(name: &str) -> String {
    format!("Hello, {}!", name)
}

/// Parses two comma-separated integers, for example "3,-4".
pub fn parse_pair(s: &str) -> Result<(i32, i32), ParseError> {
    let (a, b) = s.split_once(',').ok_or(ParseError::MissingComma)?;
    let a = a.trim().parse::<i32>().map_err(|_| ParseError::NotANumber)?;
    let b = b.trim().parse::<i32>().map_err(|_| ParseError::NotANumber)?;
    Ok((a, b))
}

/// Inverts a 3x3 matrix (row-major); a singular matrix gives the identity.
pub fn invert(m: [[f64; 3]; 3]) -> [[f64; 3]; 3] {
    let det = m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
        + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
    if det == 0.0 {
        return [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]];
    }
    let mut r = [[0.0; 3]; 3];
    for i in 0..3 {
        for j in 0..3 {
            let a = m[(j + 1) % 3][(i + 1) % 3] * m[(j + 2) % 3][(i + 2) % 3];
            let b = m[(j + 1) % 3][(i + 2) % 3] * m[(j + 2) % 3][(i + 1) % 3];
            r[i][j] = (a - b) / det;
        }
    }
    r
}

/// The index of the first element of `xs` equal to `x`, if any.
pub fn find(xs: &[i32], x: i32) -> Option<usize> {
    xs.iter().position(|v| *v == x)
}

/// Visible inside the crate only; never part of the public API.
#[allow(dead_code)]
pub(crate) fn crate_only_helper() -> u32 {
    checksum(b"crate")
}

#[allow(dead_code)]
fn private_helper() -> u32 {
    crate_only_helper() + 1
}

/// Only built for WebAssembly targets.
#[cfg(target_arch = "wasm32")]
pub fn wasm_only() -> u32 {
    private_helper()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn vec2_math() {
        let a = Vec2::new(3.0, 4.0);
        assert_eq!(a.length(), 5.0);
        assert_eq!(a.dot(Vec2::new(1.0, 0.0)), 3.0);
        assert_eq!(a.lerp(Vec2::default(), 1.0), Vec2::default());
    }

    #[test]
    fn parsing_and_lookup() {
        assert_eq!(parse_pair("3,-4"), Ok((3, -4)));
        assert_eq!(parse_pair("3"), Err(ParseError::MissingComma));
        assert_eq!(find(&[5, 6, 7], 7), Some(2));
        assert_eq!(Mode::from_name("B"), Some(Mode::B));
        assert_eq!(private_helper(), 528);
    }

    #[test]
    fn encoder_counts_frames() {
        let mut e = Encoder::new(EncoderConfig { mode: Mode::B, frame_size: 2 });
        assert_eq!(e.encode(&[0, 255, 1]), vec![255, 0, 254]);
        assert_eq!(e.frames(), 2);
        e.reset_internal();
        assert_eq!(e.frames(), 0);
        assert_eq!(invert([[2.0, 0.0, 0.0], [0.0, 2.0, 0.0], [0.0, 0.0, 2.0]])[1][1], 0.5);
    }
}
