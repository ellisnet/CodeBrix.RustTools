//! Axis-aligned shapes.

/// An axis-aligned rectangle: left, top, right, bottom edges.
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct Rect {
    /// The left edge.
    pub left: f64,
    /// The top edge.
    pub top: f64,
    /// The right edge.
    pub right: f64,
    /// The bottom edge.
    pub bottom: f64,
}

impl Rect {
    /// A rectangle at the origin with the given size.
    pub fn from_size(width: f64, height: f64) -> Rect {
        Rect { left: 0.0, top: 0.0, right: width, bottom: height }
    }

    /// The width (right minus left).
    pub fn width(&self) -> f64 {
        self.right - self.left
    }

    /// The smallest rectangle that contains this one and `other`.
    pub fn union(&self, other: &Rect) -> Rect {
        Rect {
            left: self.left.min(other.left),
            top: self.top.min(other.top),
            right: self.right.max(other.right),
            bottom: self.bottom.max(other.bottom),
        }
    }
}
