use thiserror::Error;

/// Every error the catalog can produce.
#[derive(Debug, Error)]
pub enum CatalogError {
    #[error("database error: {0}")]
    Database(#[from] rusqlite::Error),

    #[error("I/O error preparing {path}: {source}")]
    Io {
        path: String,
        #[source]
        source: std::io::Error,
    },

    #[error("invalid publication year {0} (must be between 1450 and 2100)")]
    InvalidYear(i32),

    #[error("book '{0}' not found")]
    NotFound(String),
}

pub type Result<T> = std::result::Result<T, CatalogError>;
