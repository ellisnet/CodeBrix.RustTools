//! Identifier case conversion (feature `case`, enabled by default).

/// Splits an identifier in any common style into lower-case words.
pub fn words(ident: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut current = String::new();
    let mut prev_lower = false;
    for c in ident.chars() {
        if c == '_' || c == '-' || c.is_whitespace() {
            if !current.is_empty() {
                out.push(std::mem::take(&mut current));
            }
            prev_lower = false;
            continue;
        }
        if c.is_uppercase() && prev_lower && !current.is_empty() {
            out.push(std::mem::take(&mut current));
        }
        prev_lower = c.is_lowercase() || c.is_ascii_digit();
        current.extend(c.to_lowercase());
    }
    if !current.is_empty() {
        out.push(current);
    }
    out
}

pub fn to_snake_case(ident: &str) -> String {
    words(ident).join("_")
}

pub fn to_kebab_case(ident: &str) -> String {
    words(ident).join("-")
}

pub fn to_camel_case(ident: &str) -> String {
    words(ident)
        .iter()
        .map(|w| {
            let mut chars = w.chars();
            chars.next().map(|first| first.to_uppercase().chain(chars).collect::<String>()).unwrap_or_default()
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn converts_between_styles() {
        assert_eq!(to_snake_case("HttpServerConfig"), "http_server_config");
        assert_eq!(to_kebab_case("max_retry_count"), "max-retry-count");
        assert_eq!(to_camel_case("hello-big world"), "HelloBigWorld");
    }
}
