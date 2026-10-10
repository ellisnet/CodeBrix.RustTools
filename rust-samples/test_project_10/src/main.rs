//! test_project_10: INTENTIONAL WARNINGS.
//!
//! Every warning in this file is deliberate - see README.md. Do not "fix" them and do not
//! add `#![allow(...)]`: the sample exists so a build tool can test warning capture.
//! The program itself is valid, builds, runs and passes its tests.

use std::collections::HashMap; // warning: unused import
use std::fmt::Write as _;

mod shapes;

// warning: type `my_counter` should have an upper camel case name (non_camel_case_types)
struct my_counter {
    value: u32,
}

// warning: constant `answer` should have an upper case name (non_upper_case_globals)
const answer: u32 = 42;

// warning: function `helper_never_called` is never used (dead_code)
fn helper_never_called() -> u32 {
    7
}

#[deprecated(since = "0.1.0", note = "use `greet` instead")]
fn old_greet(name: &str) -> String {
    format!("Hi {name}")
}

fn greet(name: &str) -> String {
    format!("Hello, {name}!")
}

fn parse_number(text: &str) -> Result<i32, std::num::ParseIntError> {
    text.trim().parse()
}

// warning: unreachable expression (unreachable_code)
fn early_return() -> u32 {
    return 1;
    2
}

fn main() {
    let unused_total = 10; // warning: unused variable (unused_variables)
    let mut never_mutated = 5; // warning: variable does not need to be mutable (unused_mut)

    let mut overwritten = 1; // warning: value assigned is never read (unused_assignments)
    overwritten = 2;

    let BadlyNamed = 3; // warning: variable should have a snake case name (non_snake_case)

    parse_number("123"); // warning: unused `Result` that must be used (unused_must_use)

    #[allow(unused_parens)]
    let quiet = (1 + 1); // the one place a lint is silenced locally, to show allow-on-item still works
    if (quiet > 1) { // warning: unnecessary parentheses around `if` condition (unused_parens)
        println!("{}", old_greet("world")); // warning: use of deprecated function (deprecated)
    }

    while true { // warning: denote infinite loops with `loop { ... }` (while_true)
        break;
    }

    let counter = my_counter { value: answer };
    let flag = true;
    flag; // warning: path statement with no effect (path_statements)
    let _ = counter.value;;  // warning: unnecessary trailing semicolon (redundant_semicolons)

    let mut line = String::new();
    write!(line, "{} {} {} {}", greet("Rust"), never_mutated, overwritten, BadlyNamed).unwrap();
    println!("{line}");
    println!("early_return = {}, area = {:.2}", early_return(), shapes::Shape::Circle(1.0).area());
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn greet_works() {
        assert_eq!(greet("tests"), "Hello, tests!");
    }

    #[test]
    fn deprecated_function_still_works() {
        assert_eq!(old_greet("x"), "Hi x"); // warning in test build too (deprecated)
    }

    #[test]
    fn parse_number_parses() {
        assert_eq!(parse_number(" 41 ").unwrap() + 1, answer as i32);
    }
}
