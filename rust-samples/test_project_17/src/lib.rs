//! hardcrate: a plain Rust library whose public API is made of shapes a C facade cannot map
//! automatically (generics, `impl Trait`, closures, trait objects, borrowed returns, iterators,
//! data-carrying enums, `Vec` of non-`Copy` values, `Rc` / `Arc`, `async`), plus two functions
//! that ARE mappable.

use std::rc::Rc;
use std::sync::Arc;

/// The larger of `a` and `b` (generic).
pub fn max_of<T: PartialOrd>(a: T, b: T) -> T {
    if a >= b { a } else { b }
}

/// Formats anything printable (`impl Trait` parameter).
pub fn describe(value: impl std::fmt::Display) -> String {
    format!("<{}>", value)
}

/// Calls `f` for every element (a closure parameter).
pub fn each(xs: &[i32], mut f: impl FnMut(i32)) {
    for x in xs {
        f(*x);
    }
}

/// Applies a trait object to `x`.
pub fn apply(f: &dyn Fn(i32) -> i32, x: i32) -> i32 {
    f(x)
}

/// The even numbers of `xs` (an iterator return).
pub fn evens(xs: &[i32]) -> impl Iterator<Item = i32> + '_ {
    xs.iter().copied().filter(|x| x % 2 == 0)
}

/// A shape: a data-carrying enum.
#[derive(Clone, Debug, PartialEq)]
pub enum Shape {
    /// A circle with the given radius.
    Circle(f64),
    /// A rectangle with width and height.
    Rect {
        /// The width.
        w: f64,
        /// The height.
        h: f64,
    },
}

/// The total area of `shapes` (a `Vec` of a data-carrying enum in).
pub fn total_area(shapes: Vec<Shape>) -> f64 {
    shapes
        .iter()
        .map(|s| match s {
            Shape::Circle(r) => std::f64::consts::PI * r * r,
            Shape::Rect { w, h } => w * h,
        })
        .sum()
}

/// `n` unit squares (a `Vec` of a data-carrying enum out).
pub fn unit_squares(n: usize) -> Vec<Shape> {
    vec![Shape::Rect { w: 1.0, h: 1.0 }; n]
}

/// A list of names with shared ownership inside.
#[derive(Clone, Debug, Default)]
pub struct Names {
    shared: Rc<Vec<String>>,
    /// A thread-safe tag shared between copies.
    pub tag: Arc<String>,
}

impl Names {
    /// Creates a list from `names`.
    pub fn new(names: Vec<String>) -> Names {
        Names { shared: Rc::new(names), tag: Arc::new(String::new()) }
    }

    /// The first name, borrowed from the list (a borrowed return).
    pub fn first(&self) -> &str {
        self.shared.first().map(String::as_str).unwrap_or("")
    }
}

/// A future that resolves to the doubled value (an `async` function).
pub async fn doubled_later(x: u32) -> u32 {
    x * 2
}

/// Adds two integers (mappable).
pub fn add(a: i32, b: i32) -> i32 {
    a.wrapping_add(b)
}

/// Scales `value` by `factor` (mappable).
pub fn scale(value: f64, factor: f64) -> f64 {
    value * factor
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mappable_functions() {
        assert_eq!(add(2, 3), 5);
        assert_eq!(scale(2.0, 1.5), 3.0);
    }

    #[test]
    fn unmappable_functions_still_work() {
        assert_eq!(max_of(3, 7), 7);
        assert_eq!(describe(5), "<5>");
        let mut seen = Vec::new();
        each(&[1, 2], |x| seen.push(x));
        assert_eq!(seen, vec![1, 2]);
        assert_eq!(apply(&|x| x + 1, 1), 2);
        assert_eq!(evens(&[1, 2, 3, 4]).collect::<Vec<_>>(), vec![2, 4]);
        assert_eq!(total_area(unit_squares(3)), 3.0);
        assert_eq!(Names::new(vec!["a".to_string()]).first(), "a");
    }
}
