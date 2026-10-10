//! Prints the metadata and planet table that build.rs generated.

include!(concat!(env!("OUT_DIR"), "/generated.rs"));

fn main() {
    println!("{VERSION_STRING}");
    println!("version {PKG_VERSION}, profile {BUILD_PROFILE}, target {BUILD_TARGET}, built at unix time {BUILD_UNIX_TIME}");
    println!("{:<8} {:>10} {:>12}", "planet", "radius km", "year (days)");
    for (name, radius, period) in PLANETS {
        println!("{name:<8} {radius:>10.1} {period:>12.2}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn planets_table_generated() {
        assert_eq!(PLANETS.len(), 8);
        assert_eq!(PLANETS[2].0, "Earth");
        assert_eq!(PKG_VERSION, env!("CARGO_PKG_VERSION"));
    }
}
