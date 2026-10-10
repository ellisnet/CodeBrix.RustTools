//! cargo run --example basic_stats
use textkit::{is_palindrome, stats};

fn main() {
    let samples = ["Was it a car or a cat I saw?", "The quick brown fox jumps over the lazy dog."];
    for text in samples {
        let s = stats(text);
        println!("{text:?}: {} words, longest {:?}, palindrome = {}", s.words, s.longest_word, is_palindrome(text));
    }
}
