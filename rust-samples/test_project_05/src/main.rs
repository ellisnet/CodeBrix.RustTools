//! The `textkit` binary: a thin front end over the `textkit` library in this package.

use std::io::Read;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let text = if args.is_empty() {
        let mut buf = String::new();
        if std::io::stdin().read_to_string(&mut buf).is_err() {
            eprintln!("could not read standard input");
            std::process::exit(1);
        }
        buf
    } else {
        args.join(" ")
    };

    println!("features : {:?}", textkit::enabled_features());
    let stats = textkit::stats(&text);
    println!("stats    : {stats:?}");
    println!("palindrome: {}", textkit::is_palindrome(&text));
    println!("vendor   : {:?}", textkit::vendor_info(&text));

    #[cfg(feature = "case")]
    println!("snake    : {}", textkit::case::to_snake_case(&text));

    #[cfg(feature = "json")]
    println!("json     :\n{}", textkit::json::stats_to_json(&stats));
}
