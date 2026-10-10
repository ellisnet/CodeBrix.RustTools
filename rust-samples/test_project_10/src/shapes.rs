//! A second module so warnings are reported against more than one file.

pub enum Shape {
    Circle(f64),
    Square(f64),   // warning: variant `Square` is never constructed (dead_code)
}

pub struct Unused {      // warning: struct `Unused` is never constructed (dead_code)
    pub name: String,
}

impl Shape {
    pub fn area(&self) -> f64 {
        let unused_scale = 2.0; // warning: unused variable
        match self {
            Shape::Circle(r) => std::f64::consts::PI * r * r,
            Shape::Square(side) => side * side,
        }
    }

    pub fn is_small(&self) -> bool {      // warning: method `is_small` is never used (dead_code)
        let width: u8 = 3;
        width >= 0 && self.area() < 1.0    // warning: comparison is useless due to type limits (unused_comparisons)
    }
}
