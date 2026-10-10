use rusqlite::{Connection, OptionalExtension, params};

use crate::error::{CatalogError, Result};

#[derive(Debug, Clone, PartialEq)]
pub struct Book {
    pub id: i64,
    pub title: String,
    pub author: String,
    pub year: i32,
    pub rating: f64,
}

pub struct Catalog {
    conn: Connection,
}

impl Catalog {
    pub fn open(path: &std::path::Path) -> Result<Self> {
        Self::init(Connection::open(path)?)
    }

    #[cfg(test)]
    pub fn open_in_memory() -> Result<Self> {
        Self::init(Connection::open_in_memory()?)
    }

    fn init(conn: Connection) -> Result<Self> {
        conn.execute_batch(
            "PRAGMA foreign_keys = ON;
             CREATE TABLE IF NOT EXISTS authors (
                 id   INTEGER PRIMARY KEY,
                 name TEXT NOT NULL UNIQUE
             );
             CREATE TABLE IF NOT EXISTS books (
                 id        INTEGER PRIMARY KEY,
                 title     TEXT NOT NULL,
                 author_id INTEGER NOT NULL REFERENCES authors(id),
                 year      INTEGER NOT NULL,
                 rating    REAL NOT NULL DEFAULT 0
             );
             CREATE INDEX IF NOT EXISTS idx_books_year ON books(year);",
        )?;
        Ok(Self { conn })
    }

    pub fn sqlite_version() -> &'static str {
        rusqlite::version()
    }

    pub fn clear(&self) -> Result<()> {
        self.conn.execute_batch("DELETE FROM books; DELETE FROM authors;")?;
        Ok(())
    }

    /// Inserts a book (and its author if new) inside a transaction.
    pub fn add_book(&mut self, title: &str, author: &str, year: i32, rating: f64) -> Result<i64> {
        if !(1450..=2100).contains(&year) {
            return Err(CatalogError::InvalidYear(year));
        }
        let tx = self.conn.transaction()?;
        tx.execute("INSERT OR IGNORE INTO authors (name) VALUES (?1)", params![author])?;
        let author_id: i64 = tx.query_row("SELECT id FROM authors WHERE name = ?1", params![author], |r| r.get(0))?;
        tx.execute(
            "INSERT INTO books (title, author_id, year, rating) VALUES (?1, ?2, ?3, ?4)",
            params![title, author_id, year, rating],
        )?;
        let id = tx.last_insert_rowid();
        tx.commit()?;
        Ok(id)
    }

    pub fn find_by_title(&self, title: &str) -> Result<Book> {
        self.conn
            .query_row(
                "SELECT b.id, b.title, a.name, b.year, b.rating FROM books b JOIN authors a ON a.id = b.author_id WHERE b.title = ?1",
                params![title],
                row_to_book,
            )
            .optional()?
            .ok_or_else(|| CatalogError::NotFound(title.to_string()))
    }

    pub fn published_before(&self, year: i32) -> Result<Vec<Book>> {
        let mut stmt = self.conn.prepare(
            "SELECT b.id, b.title, a.name, b.year, b.rating FROM books b JOIN authors a ON a.id = b.author_id
             WHERE b.year < ?1 ORDER BY b.year",
        )?;
        let books = stmt.query_map(params![year], row_to_book)?.collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(books)
    }

    /// (author, number of books, average rating), most books first.
    pub fn author_stats(&self) -> Result<Vec<(String, i64, f64)>> {
        let mut stmt = self.conn.prepare(
            "SELECT a.name, COUNT(*), AVG(b.rating) FROM books b JOIN authors a ON a.id = b.author_id
             GROUP BY a.id ORDER BY COUNT(*) DESC, a.name",
        )?;
        let rows = stmt.query_map([], |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)))?.collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(rows)
    }

    pub fn update_rating(&self, title: &str, rating: f64) -> Result<()> {
        match self.conn.execute("UPDATE books SET rating = ?1 WHERE title = ?2", params![rating, title])? {
            0 => Err(CatalogError::NotFound(title.to_string())),
            _ => Ok(()),
        }
    }

    pub fn count(&self) -> Result<i64> {
        Ok(self.conn.query_row("SELECT COUNT(*) FROM books", [], |r| r.get(0))?)
    }
}

fn row_to_book(row: &rusqlite::Row<'_>) -> rusqlite::Result<Book> {
    Ok(Book { id: row.get(0)?, title: row.get(1)?, author: row.get(2)?, year: row.get(3)?, rating: row.get(4)? })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn seeded() -> Catalog {
        let mut c = Catalog::open_in_memory().unwrap();
        c.add_book("Emma", "Jane Austen", 1815, 4.0).unwrap();
        c.add_book("Persuasion", "Jane Austen", 1817, 4.5).unwrap();
        c.add_book("Middlemarch", "George Eliot", 1871, 4.2).unwrap();
        c
    }

    #[test]
    fn inserts_and_queries() {
        let c = seeded();
        assert_eq!(c.count().unwrap(), 3);
        let emma = c.find_by_title("Emma").unwrap();
        assert_eq!(emma.author, "Jane Austen");
        assert_eq!(c.published_before(1820).unwrap().len(), 2);
    }

    #[test]
    fn aggregates_by_author() {
        let stats = seeded().author_stats().unwrap();
        assert_eq!(stats[0], ("Jane Austen".to_string(), 2, 4.25));
    }

    #[test]
    fn reports_typed_errors() {
        let mut c = seeded();
        assert!(matches!(c.add_book("Future", "Nobody", 3000, 1.0), Err(CatalogError::InvalidYear(3000))));
        assert!(matches!(c.find_by_title("Missing"), Err(CatalogError::NotFound(_))));
        assert!(matches!(c.update_rating("Missing", 1.0), Err(CatalogError::NotFound(_))));
    }

    #[test]
    fn bundled_sqlite_is_modern() {
        assert!(Catalog::sqlite_version().starts_with("3."));
    }
}
