//! Pure image generators; each returns an in-memory `RgbImage`.

use image::{Rgb, RgbImage};

fn lerp(a: u8, b: u8, t: f64) -> u8 {
    (f64::from(a) + (f64::from(b) - f64::from(a)) * t.clamp(0.0, 1.0)).round() as u8
}

pub fn lerp_color(a: [u8; 3], b: [u8; 3], t: f64) -> Rgb<u8> {
    Rgb([lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)])
}

/// Samples a multi-stop palette at position `t` in 0..=1.
pub fn palette_at(palette: &[[u8; 3]], t: f64) -> Rgb<u8> {
    let segments = (palette.len() - 1) as f64;
    let pos = t.clamp(0.0, 1.0) * segments;
    let index = (pos.floor() as usize).min(palette.len() - 2);
    lerp_color(palette[index], palette[index + 1], pos - index as f64)
}

/// Diagonal linear gradient from `start` (top-left) to `end` (bottom-right).
pub fn gradient(width: u32, height: u32, start: [u8; 3], end: [u8; 3]) -> RgbImage {
    let max = f64::from(width + height - 2).max(1.0);
    RgbImage::from_fn(width, height, |x, y| lerp_color(start, end, f64::from(x + y) / max))
}

/// Escape-time iteration count with smooth colouring; `None` means inside the set.
pub fn mandelbrot_escape(cx: f64, cy: f64, max_iterations: u32) -> Option<f64> {
    let (mut zx, mut zy) = (0.0f64, 0.0f64);
    for i in 0..max_iterations {
        let (x2, y2) = (zx * zx, zy * zy);
        if x2 + y2 > 256.0 {
            let log_zn = (x2 + y2).ln() / 2.0;
            let nu = (log_zn / std::f64::consts::LN_2).ln() / std::f64::consts::LN_2;
            return Some(f64::from(i) + 1.0 - nu);
        }
        zy = 2.0 * zx * zy + cy;
        zx = x2 - y2 + cx;
    }
    None
}

pub fn mandelbrot(width: u32, height: u32, center: (f64, f64), zoom: f64, max_iterations: u32, palette: &[[u8; 3]]) -> RgbImage {
    let scale = 3.0 / (zoom * f64::from(width));
    RgbImage::from_fn(width, height, |x, y| {
        let cx = center.0 + (f64::from(x) - f64::from(width) / 2.0) * scale;
        let cy = center.1 + (f64::from(y) - f64::from(height) / 2.0) * scale;
        match mandelbrot_escape(cx, cy, max_iterations) {
            Some(n) => palette_at(palette, (n / f64::from(max_iterations)).sqrt()),
            None => Rgb([0, 0, 0]),
        }
    })
}

/// Classic sine-sum plasma.
pub fn plasma(width: u32, height: u32, scale: f64, seed: u32, palette: &[[u8; 3]]) -> RgbImage {
    let phase = f64::from(seed) * 0.731;
    RgbImage::from_fn(width, height, |x, y| {
        let (fx, fy) = (f64::from(x) * scale, f64::from(y) * scale);
        let v = (fx + phase).sin()
            + (fy * 1.3 - phase).sin()
            + ((fx + fy) * 0.7).sin()
            + ((fx * fx + fy * fy).sqrt() * 0.9 + phase).sin();
        palette_at(palette, (v + 4.0) / 8.0)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gradient_corners() {
        let img = gradient(5, 5, [0, 0, 0], [200, 100, 50]);
        assert_eq!(img.get_pixel(0, 0), &Rgb([0, 0, 0]));
        assert_eq!(img.get_pixel(4, 4), &Rgb([200, 100, 50]));
    }

    #[test]
    fn mandelbrot_membership() {
        assert_eq!(mandelbrot_escape(0.0, 0.0, 100), None);
        assert!(mandelbrot_escape(2.0, 2.0, 100).is_some());
    }

    #[test]
    fn palette_endpoints() {
        let p = [[0, 0, 0], [100, 100, 100], [200, 0, 0]];
        assert_eq!(palette_at(&p, 0.0), Rgb([0, 0, 0]));
        assert_eq!(palette_at(&p, 0.5), Rgb([100, 100, 100]));
        assert_eq!(palette_at(&p, 1.0), Rgb([200, 0, 0]));
    }

    #[test]
    fn png_round_trip_in_memory() {
        let img = plasma(16, 8, 0.2, 1, &[[0, 0, 0], [255, 255, 255]]);
        let mut bytes = std::io::Cursor::new(Vec::new());
        img.write_to(&mut bytes, image::ImageFormat::Png).unwrap();
        let decoded = image::load_from_memory(bytes.get_ref()).unwrap().to_rgb8();
        assert_eq!(decoded, img);
    }
}
