//! `unitconv 12.5km mi` - converts a quantity to another unit.

use anyhow::Result;
use clap::Parser;
use unitconv_core::{ALL_UNITS, Quantity, Unit};

#[derive(Parser, Debug)]
#[command(name = "unitconv", version, about = "Convert between units of length, mass and temperature")]
struct Args {
    /// Quantity to convert, e.g. "12.5km" or "98.6F"
    #[arg(required_unless_present = "list")]
    quantity: Option<Quantity>,
    /// Target unit symbol, e.g. "mi"
    #[arg(required_unless_present = "list")]
    to: Option<Unit>,
    /// List the supported unit symbols
    #[arg(long)]
    list: bool,
}

fn main() -> Result<()> {
    let args = Args::parse();
    if args.list {
        for unit in ALL_UNITS {
            println!("{:<3} {:?} ({:?})", unit.symbol(), unit, unit.dimension());
        }
        return Ok(());
    }
    let (quantity, to) = (args.quantity.expect("required by clap"), args.to.expect("required by clap"));
    println!("{quantity} = {}", quantity.convert_to(to)?);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use clap::CommandFactory;

    #[test]
    fn cli_is_well_formed() {
        Args::command().debug_assert();
    }

    #[test]
    fn parses_positional_values() {
        let args = Args::try_parse_from(["unitconv", "3ft", "m"]).unwrap();
        assert_eq!(args.quantity.unwrap(), Quantity::new(3.0, Unit::Foot));
        assert_eq!(args.to.unwrap(), Unit::Meter);
    }
}
