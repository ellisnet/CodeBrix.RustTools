//! A small reader for the parts of Cargo.toml the extractor needs ([package], [lib], [[bin]],
//! [features], [workspace]). It understands the TOML that Cargo manifests use - tables, array
//! tables, dotted and quoted keys, strings (basic, literal, multi-line), arrays, inline tables,
//! booleans, numbers and dates - and keeps every value as data; it never evaluates anything.

use std::collections::BTreeMap;

/// One TOML value.
#[derive(Clone, Debug)]
pub enum Value {
    Str(String),
    Bool(bool),
    Array(Vec<Value>),
    /// A number, date or anything else, as written.
    Other(#[allow(dead_code)] String),
}

impl Value {
    pub fn as_str(&self) -> Option<&str> {
        match self {
            Value::Str(s) => Some(s),
            _ => None,
        }
    }

    pub fn as_bool(&self) -> Option<bool> {
        match self {
            Value::Bool(b) => Some(*b),
            _ => None,
        }
    }

    pub fn as_str_array(&self) -> Option<Vec<String>> {
        match self {
            Value::Array(items) => Some(items.iter().filter_map(|v| v.as_str().map(str::to_string)).collect()),
            _ => None,
        }
    }
}

/// Every key/value of the document, keyed by the full dotted path. Array tables get their
/// index as a path element ("bin", "0", "path").
#[derive(Default, Debug)]
pub struct Document {
    pub entries: BTreeMap<Vec<String>, Value>,
    pub tables: Vec<Vec<String>>,
}

impl Document {
    pub fn get(&self, path: &[&str]) -> Option<&Value> {
        let key: Vec<String> = path.iter().map(|s| s.to_string()).collect();
        self.entries.get(&key)
    }

    pub fn get_str(&self, path: &[&str]) -> Option<String> {
        self.get(path).and_then(|v| v.as_str()).map(str::to_string)
    }

    /// The distinct key names directly below a table (for [features]).
    pub fn keys_below(&self, table: &str) -> Vec<String> {
        let mut names: Vec<String> = Vec::new();
        for key in self.entries.keys() {
            if key.len() >= 2 && key[0] == table && !names.contains(&key[1]) {
                names.push(key[1].clone());
            }
        }
        names
    }

    /// The number of [[name]] array tables.
    pub fn array_table_count(&self, name: &str) -> usize {
        let mut count = 0usize;
        for key in self.entries.keys() {
            if key.len() >= 3 && key[0] == name
                && let Ok(index) = key[1].parse::<usize>() {
                    count = count.max(index + 1);
                }
        }
        count
    }
}

pub fn parse(text: &str) -> Result<Document, String> {
    let mut parser = Parser { chars: text.chars().collect(), pos: 0, line: 1 };
    let mut doc = Document::default();
    let mut current: Vec<String> = Vec::new();
    let mut array_counts: BTreeMap<Vec<String>, usize> = BTreeMap::new();
    loop {
        parser.skip_trivia(true);
        let Some(c) = parser.peek() else { break };
        if c == '[' {
            parser.pos += 1;
            let is_array = parser.peek() == Some('[');
            if is_array {
                parser.pos += 1;
            }
            parser.skip_trivia(false);
            let path = parser.key_path()?;
            parser.skip_trivia(false);
            parser.expect(']')?;
            if is_array {
                parser.expect(']')?;
                let index = array_counts.entry(path.clone()).or_insert(0);
                let mut full = path.clone();
                full.push(index.to_string());
                *index += 1;
                current = full;
            } else {
                current = path;
            }
            doc.tables.push(current.clone());
        } else {
            let key = parser.key_path()?;
            parser.skip_trivia(false);
            parser.expect('=')?;
            parser.skip_trivia(false);
            let mut full = current.clone();
            full.extend(key);
            parser.value_into(&full, &mut doc)?;
        }
    }
    Ok(doc)
}

struct Parser {
    chars: Vec<char>,
    pos: usize,
    line: usize,
}

impl Parser {
    fn peek(&self) -> Option<char> {
        self.chars.get(self.pos).copied()
    }

    fn peek_at(&self, offset: usize) -> Option<char> {
        self.chars.get(self.pos + offset).copied()
    }

    fn error(&self, message: &str) -> String {
        format!("Cargo.toml line {}: {}", self.line, message)
    }

    fn expect(&mut self, c: char) -> Result<(), String> {
        if self.peek() == Some(c) {
            self.pos += 1;
            Ok(())
        } else {
            Err(self.error(&format!("expected '{}'", c)))
        }
    }

    /// Skips spaces, tabs and comments; with `newlines` also line breaks.
    fn skip_trivia(&mut self, newlines: bool) {
        while let Some(c) = self.peek() {
            if c == ' ' || c == '\t' || c == '\r' {
                self.pos += 1;
            } else if c == '\n' && newlines {
                self.line += 1;
                self.pos += 1;
            } else if c == '#' {
                while let Some(c) = self.peek() {
                    if c == '\n' {
                        break;
                    }
                    self.pos += 1;
                }
            } else {
                break;
            }
        }
    }

    fn key_path(&mut self) -> Result<Vec<String>, String> {
        let mut parts = Vec::new();
        loop {
            self.skip_trivia(false);
            let part = match self.peek() {
                Some('"') => self.basic_string()?,
                Some('\'') => self.literal_string()?,
                _ => {
                    let start = self.pos;
                    while let Some(c) = self.peek() {
                        if c.is_ascii_alphanumeric() || c == '_' || c == '-' {
                            self.pos += 1;
                        } else {
                            break;
                        }
                    }
                    if start == self.pos {
                        return Err(self.error("expected a key"));
                    }
                    self.chars[start..self.pos].iter().collect()
                }
            };
            parts.push(part);
            self.skip_trivia(false);
            if self.peek() == Some('.') {
                self.pos += 1;
            } else {
                break;
            }
        }
        Ok(parts)
    }

    fn value_into(&mut self, path: &[String], doc: &mut Document) -> Result<(), String> {
        if self.peek() == Some('{') {
            self.pos += 1;
            loop {
                self.skip_trivia(true);
                if self.peek() == Some('}') {
                    self.pos += 1;
                    break;
                }
                let key = self.key_path()?;
                self.skip_trivia(false);
                self.expect('=')?;
                self.skip_trivia(false);
                let mut full = path.to_vec();
                full.extend(key);
                self.value_into(&full, doc)?;
                self.skip_trivia(true);
                if self.peek() == Some(',') {
                    self.pos += 1;
                }
            }
            return Ok(());
        }
        let value = self.value()?;
        doc.entries.insert(path.to_vec(), value);
        Ok(())
    }

    fn value(&mut self) -> Result<Value, String> {
        match self.peek() {
            Some('"') => Ok(Value::Str(self.basic_string()?)),
            Some('\'') => Ok(Value::Str(self.literal_string()?)),
            Some('[') => {
                self.pos += 1;
                let mut items = Vec::new();
                loop {
                    self.skip_trivia(true);
                    if self.peek() == Some(']') {
                        self.pos += 1;
                        break;
                    }
                    if self.peek() == Some('{') {
                        // An inline table inside an array: kept as raw text.
                        let start = self.pos;
                        let mut depth = 0;
                        while let Some(c) = self.peek() {
                            self.pos += 1;
                            if c == '{' {
                                depth += 1;
                            } else if c == '}' {
                                depth -= 1;
                                if depth == 0 {
                                    break;
                                }
                            }
                        }
                        items.push(Value::Other(self.chars[start..self.pos].iter().collect()));
                    } else {
                        items.push(self.value()?);
                    }
                    self.skip_trivia(true);
                    if self.peek() == Some(',') {
                        self.pos += 1;
                    }
                }
                Ok(Value::Array(items))
            }
            Some(_) => {
                let start = self.pos;
                while let Some(c) = self.peek() {
                    if c == ',' || c == ']' || c == '}' || c == '\n' || c == '#' {
                        break;
                    }
                    self.pos += 1;
                }
                let raw: String = self.chars[start..self.pos].iter().collect();
                let raw = raw.trim().to_string();
                match raw.as_str() {
                    "true" => Ok(Value::Bool(true)),
                    "false" => Ok(Value::Bool(false)),
                    "" => Err(self.error("expected a value")),
                    _ => Ok(Value::Other(raw)),
                }
            }
            None => Err(self.error("unexpected end of file")),
        }
    }

    fn basic_string(&mut self) -> Result<String, String> {
        if self.peek_at(1) == Some('"') && self.peek_at(2) == Some('"') {
            self.pos += 3;
            if self.peek() == Some('\n') {
                self.line += 1;
                self.pos += 1;
            }
            let mut out = String::new();
            loop {
                match self.peek() {
                    None => return Err(self.error("unterminated string")),
                    Some('"') if self.peek_at(1) == Some('"') && self.peek_at(2) == Some('"') => {
                        self.pos += 3;
                        while self.peek() == Some('"') {
                            out.push('"');
                            self.pos += 1;
                        }
                        return Ok(out);
                    }
                    Some('\\') => {
                        self.pos += 1;
                        if matches!(self.peek(), Some('\n') | Some(' ') | Some('\t') | Some('\r')) {
                            while matches!(self.peek(), Some('\n') | Some(' ') | Some('\t') | Some('\r')) {
                                if self.peek() == Some('\n') {
                                    self.line += 1;
                                }
                                self.pos += 1;
                            }
                        } else {
                            out.push(self.escape()?);
                        }
                    }
                    Some(c) => {
                        if c == '\n' {
                            self.line += 1;
                        }
                        out.push(c);
                        self.pos += 1;
                    }
                }
            }
        }
        self.pos += 1;
        let mut out = String::new();
        loop {
            match self.peek() {
                None | Some('\n') => return Err(self.error("unterminated string")),
                Some('"') => {
                    self.pos += 1;
                    return Ok(out);
                }
                Some('\\') => {
                    self.pos += 1;
                    out.push(self.escape()?);
                }
                Some(c) => {
                    out.push(c);
                    self.pos += 1;
                }
            }
        }
    }

    fn escape(&mut self) -> Result<char, String> {
        let c = self.peek().ok_or_else(|| self.error("unterminated escape"))?;
        self.pos += 1;
        Ok(match c {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            'b' => '\u{8}',
            'f' => '\u{c}',
            'e' => '\u{1b}',
            '"' => '"',
            '\\' => '\\',
            'u' | 'U' => {
                let len = if c == 'u' { 4 } else { 8 };
                let hex: String = self.chars[self.pos..(self.pos + len).min(self.chars.len())].iter().collect();
                self.pos += len;
                u32::from_str_radix(&hex, 16).ok().and_then(char::from_u32).unwrap_or('\u{fffd}')
            }
            other => other,
        })
    }

    fn literal_string(&mut self) -> Result<String, String> {
        if self.peek_at(1) == Some('\'') && self.peek_at(2) == Some('\'') {
            self.pos += 3;
            if self.peek() == Some('\n') {
                self.line += 1;
                self.pos += 1;
            }
            let start = self.pos;
            loop {
                match self.peek() {
                    None => return Err(self.error("unterminated string")),
                    Some('\'') if self.peek_at(1) == Some('\'') && self.peek_at(2) == Some('\'') => {
                        let out: String = self.chars[start..self.pos].iter().collect();
                        self.pos += 3;
                        return Ok(out);
                    }
                    Some(c) => {
                        if c == '\n' {
                            self.line += 1;
                        }
                        self.pos += 1;
                    }
                }
            }
        }
        self.pos += 1;
        let start = self.pos;
        loop {
            match self.peek() {
                None | Some('\n') => return Err(self.error("unterminated string")),
                Some('\'') => {
                    let out: String = self.chars[start..self.pos].iter().collect();
                    self.pos += 1;
                    return Ok(out);
                }
                Some(_) => self.pos += 1,
            }
        }
    }
}
