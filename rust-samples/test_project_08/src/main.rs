//! test_project_08: builds a small SQLite book catalog in `output/catalog.db`
//! inside this sample's folder, then runs a few queries.

mod catalog;
mod error;

use std::path::Path;
use std::process::ExitCode;

use catalog::Catalog;
use error::{CatalogError, Result};

const BOOKS: &[(&str, &str, i32, f64)] = &[
    ("Pride and Prejudice", "Jane Austen", 1813, 4.6),
    ("Emma", "Jane Austen", 1815, 4.1),
    ("Moby-Dick", "Herman Melville", 1851, 3.9),
    ("Bartleby, the Scrivener", "Herman Melville", 1853, 4.0),
    ("Great Expectations", "Charles Dickens", 1861, 4.4),
    ("A Tale of Two Cities", "Charles Dickens", 1859, 4.3),
    ("Middlemarch", "George Eliot", 1871, 4.2),
    ("Frankenstein", "Mary Shelley", 1818, 4.0),
];

fn run() -> Result<()> {
    let out_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("output");
    std::fs::create_dir_all(&out_dir).map_err(|source| CatalogError::Io { path: out_dir.display().to_string(), source })?;
    let db_path = out_dir.join("catalog.db");

    let mut catalog = Catalog::open(&db_path)?;
    println!("SQLite {} (bundled) -> {}", Catalog::sqlite_version(), db_path.display());
    catalog.clear()?;
    for &(title, author, year, rating) in BOOKS {
        catalog.add_book(title, author, year, rating)?;
    }
    catalog.update_rating("Moby-Dick", 4.2)?;
    println!("{} books stored", catalog.count()?);

    println!("\nPublished before 1850:");
    for book in catalog.published_before(1850)? {
        println!("  {} ({}) by {} - rating {:.1}", book.title, book.year, book.author, book.rating);
    }

    println!("\nAuthors:");
    for (author, books, avg) in catalog.author_stats()? {
        println!("  {author:<16} {books} book(s), average rating {avg:.2}");
    }

    // Demonstrate a handled, typed error.
    match catalog.find_by_title("The Lost Manuscript") {
        Err(e @ CatalogError::NotFound(_)) => println!("\nExpected lookup failure: {e}"),
        other => println!("\nUnexpected result: {other:?}"),
    }
    Ok(())
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}
