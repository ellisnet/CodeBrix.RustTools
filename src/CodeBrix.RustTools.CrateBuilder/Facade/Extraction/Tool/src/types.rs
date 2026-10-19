//! Types and generics as data, and a token printer with rustfmt-like spacing.

use proc_macro2::{Delimiter, Spacing, TokenStream, TokenTree};
use quote::ToTokens;
use syn::{GenericArgument, GenericParam, Generics, PathArguments, ReturnType, Type};

use crate::model::{
    AssocBindingInfo, ConstParamInfo, GenericsInfo, PathSegmentInfo, TypeInfo, TypeKind, TypeParamInfo,
};

const PRIMITIVES: &[&str] = &[
    "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f16", "f32", "f64",
    "f128", "bool", "char", "str",
];

/// Prints anything syn can turn into tokens.
pub fn print<T: ToTokens>(value: &T) -> String {
    print_tokens(value.to_token_stream())
}

#[derive(Clone, PartialEq)]
enum Atom {
    Word(String),
    Op(String),
    Group(Delimiter, String),
}

/// Prints a token stream with the spacing rustfmt would mostly use: `&[u8]`, `Vec<u8>`,
/// `[[f64; 3]; 3]`, `impl FnMut(i32)`, `T: PartialOrd + Copy`, `-> Option<usize>`.
pub fn print_tokens(stream: TokenStream) -> String {
    let atoms = to_atoms(stream);
    let mut out = String::new();
    let mut prev: Option<Atom> = None;
    for atom in atoms {
        if let Some(p) = &prev
            && needs_space(p, &atom) {
                out.push(' ');
            }
        match &atom {
            Atom::Word(w) | Atom::Op(w) => out.push_str(w),
            Atom::Group(d, inner) => {
                let (open, close) = match d {
                    Delimiter::Parenthesis => ("(", ")"),
                    Delimiter::Bracket => ("[", "]"),
                    Delimiter::Brace => ("{", "}"),
                    Delimiter::None => ("", ""),
                };
                out.push_str(open);
                if *d == Delimiter::Brace && !inner.is_empty() {
                    out.push(' ');
                    out.push_str(inner);
                    out.push(' ');
                } else {
                    out.push_str(inner);
                }
                out.push_str(close);
            }
        }
        prev = Some(atom);
    }
    out
}

fn to_atoms(stream: TokenStream) -> Vec<Atom> {
    let mut atoms = Vec::new();
    let mut op = String::new();
    let mut lifetime = false;
    for tree in stream {
        match tree {
            TokenTree::Punct(p) => {
                if p.as_char() == '\'' && op.is_empty() {
                    lifetime = true;
                    continue;
                }
                op.push(p.as_char());
                if p.spacing() == Spacing::Alone {
                    atoms.push(Atom::Op(std::mem::take(&mut op)));
                }
            }
            other => {
                if !op.is_empty() {
                    atoms.push(Atom::Op(std::mem::take(&mut op)));
                }
                match other {
                    TokenTree::Ident(i) => {
                        let text = i.to_string();
                        if lifetime {
                            atoms.push(Atom::Word(format!("'{}", text)));
                            lifetime = false;
                        } else {
                            atoms.push(Atom::Word(text));
                        }
                    }
                    TokenTree::Literal(l) => atoms.push(Atom::Word(l.to_string())),
                    TokenTree::Group(g) => {
                        if g.delimiter() == Delimiter::None {
                            atoms.extend(to_atoms(g.stream()));
                        } else {
                            atoms.push(Atom::Group(g.delimiter(), print_tokens(g.stream())));
                        }
                    }
                    TokenTree::Punct(_) => {}
                }
            }
        }
    }
    if !op.is_empty() {
        atoms.push(Atom::Op(op));
    }
    atoms
}

fn is_spaced_op(op: &str) -> bool {
    matches!(
        op,
        "->" | "=>" | "=" | "+" | "==" | "!=" | "+=" | "-=" | "*=" | "/=" | "|" | "||" | "&&" | ">=" | "<=" | "/" | "%"
            | "^" | "<<" | ">>" | "|="
    )
}

fn needs_space(prev: &Atom, cur: &Atom) -> bool {
    match (prev, cur) {
        (_, Atom::Op(c)) if matches!(c.as_str(), "," | ";" | ":" | "::" | "." | ".." | "..=" | ">" | "?") => {
            // `?Sized` after `:` / `+` gets its space from the previous operator.
            c == "?" && matches!(prev, Atom::Op(p) if p == ":" || p == "+" || p == ",")
        }
        (Atom::Op(p), _) if matches!(p.as_str(), "," | ";" | ":") => true,
        (Atom::Op(p), _) if is_spaced_op(p) => true,
        (_, Atom::Op(c)) if is_spaced_op(c) => true,
        (Atom::Op(p), _) if matches!(p.as_str(), "::" | "&" | "*" | "!" | "#" | "#!" | "$" | "." | ".." | "?" | "<" | "-") => false,
        (Atom::Op(p), Atom::Word(_)) if p == ">" => true,
        (Atom::Op(p), Atom::Op(_)) if p == ">" => false,
        (Atom::Op(_), Atom::Group(d, _)) => *d == Delimiter::Brace,
        (_, Atom::Op(c)) if c == "<" => false,
        (_, Atom::Op(c)) if c == "!" => !matches!(prev, Atom::Word(_)),
        (_, Atom::Op(c)) if c == "&" || c == "*" || c == "-" => matches!(prev, Atom::Word(_) | Atom::Group(..)),
        (Atom::Word(w), Atom::Group(d, _)) => *d == Delimiter::Brace || is_keyword(w),
        (Atom::Group(..), Atom::Group(d, _)) => *d == Delimiter::Brace,
        (Atom::Group(..), Atom::Word(_)) => true,
        (Atom::Word(_), Atom::Word(_)) => true,
        (Atom::Op(_), Atom::Word(_)) => true,
        _ => true,
    }
}

/// Keywords that are followed by a space even before `(` or `[` (`&mut [u8]`, `*const (A, B)`).
fn is_keyword(word: &str) -> bool {
    matches!(word, "mut" | "const" | "dyn" | "impl" | "as" | "in" | "where")
}

pub fn is_primitive(name: &str) -> bool {
    PRIMITIVES.contains(&name)
}

/// The structured view of a type.
pub fn type_info(ty: &Type) -> TypeInfo {
    match ty {
        Type::Paren(p) => type_info(&p.elem),
        Type::Group(g) => type_info(&g.elem),
        Type::Reference(r) => {
            let mut t = TypeInfo::new(TypeKind::Reference, print(ty));
            t.mutable = Some(r.mutability.is_some());
            t.lifetime = r.lifetime.as_ref().map(|l| l.to_string());
            t.inner = Some(Box::new(type_info(&r.elem)));
            t
        }
        Type::Slice(s) => {
            let mut t = TypeInfo::new(TypeKind::Slice, print(ty));
            t.elem = Some(Box::new(type_info(&s.elem)));
            t
        }
        Type::Array(a) => {
            let mut t = TypeInfo::new(TypeKind::Array, print(ty));
            t.elem = Some(Box::new(type_info(&a.elem)));
            let len = print(&a.len);
            t.len_value = parse_unsigned(&len);
            t.len = Some(len);
            t
        }
        Type::Tuple(tuple) => {
            if tuple.elems.is_empty() {
                TypeInfo::unit()
            } else {
                let mut t = TypeInfo::new(TypeKind::Tuple, print(ty));
                t.elems = Some(tuple.elems.iter().map(type_info).collect());
                t
            }
        }
        Type::Ptr(p) => {
            let mut t = TypeInfo::new(TypeKind::Ptr, print(ty));
            t.mutable = Some(p.mutability.is_some());
            t.inner = Some(Box::new(type_info(&p.elem)));
            t
        }
        Type::ImplTrait(i) => {
            let mut t = TypeInfo::new(TypeKind::ImplTrait, print(ty));
            t.bounds = Some(i.bounds.iter().map(print).collect());
            t
        }
        Type::TraitObject(d) => {
            let mut t = TypeInfo::new(TypeKind::DynTrait, print(ty));
            t.bounds = Some(d.bounds.iter().map(print).collect());
            t
        }
        Type::BareFn(f) => {
            let mut t = TypeInfo::new(TypeKind::FnPtr, print(ty));
            t.inputs = Some(f.inputs.iter().map(|a| type_info(&a.ty)).collect());
            t.output = Some(Box::new(return_type(&f.output)));
            t.is_unsafe = Some(f.unsafety.is_some());
            t.abi = f.abi.as_ref().map(abi_name);
            t
        }
        Type::Never(_) => TypeInfo::new(TypeKind::Never, "!".to_string()),
        Type::Infer(_) => TypeInfo::new(TypeKind::Infer, "_".to_string()),
        Type::Macro(_) => TypeInfo::new(TypeKind::Macro, print(ty)),
        Type::Path(p) => {
            if p.qself.is_none()
                && p.path.leading_colon.is_none()
                && p.path.segments.len() == 1
                && matches!(p.path.segments[0].arguments, PathArguments::None)
                && is_primitive(&p.path.segments[0].ident.to_string())
            {
                let mut t = TypeInfo::new(TypeKind::Primitive, print(ty));
                t.name = Some(p.path.segments[0].ident.to_string());
                return t;
            }
            let mut t = TypeInfo::new(TypeKind::Path, print(ty));
            t.qself = p.qself.as_ref().map(|q| Box::new(type_info(&q.ty)));
            t.segments = Some(p.path.segments.iter().map(segment_info).collect());
            t
        }
        _ => TypeInfo::new(TypeKind::Other, print(ty)),
    }
}

fn segment_info(segment: &syn::PathSegment) -> PathSegmentInfo {
    let mut info = PathSegmentInfo {
        name: segment.ident.to_string(),
        args: Vec::new(),
        lifetimes: Vec::new(),
        const_args: Vec::new(),
        bindings: Vec::new(),
        other_args: Vec::new(),
        fn_inputs: None,
        fn_output: None,
    };
    match &segment.arguments {
        PathArguments::None => {}
        PathArguments::AngleBracketed(a) => {
            for arg in &a.args {
                match arg {
                    GenericArgument::Type(t) => info.args.push(type_info(t)),
                    GenericArgument::Lifetime(l) => info.lifetimes.push(l.to_string()),
                    GenericArgument::Const(c) => info.const_args.push(print(c)),
                    GenericArgument::AssocType(b) => info.bindings.push(AssocBindingInfo {
                        name: b.ident.to_string(),
                        ty: type_info(&b.ty),
                    }),
                    other => info.other_args.push(print(other)),
                }
            }
        }
        PathArguments::Parenthesized(p) => {
            info.fn_inputs = Some(p.inputs.iter().map(type_info).collect());
            info.fn_output = Some(Box::new(return_type(&p.output)));
        }
    }
    info
}

pub fn return_type(output: &ReturnType) -> TypeInfo {
    match output {
        ReturnType::Default => TypeInfo::unit(),
        ReturnType::Type(_, ty) => type_info(ty),
    }
}

pub fn abi_name(abi: &syn::Abi) -> String {
    abi.name.as_ref().map(|n| n.value()).unwrap_or_else(|| "C".to_string())
}

pub fn generics_info(generics: &Generics) -> GenericsInfo {
    let mut info = GenericsInfo {
        text: if generics.params.is_empty() { String::new() } else { print_generic_params(generics) },
        where_clause: generics.where_clause.as_ref().map(print).unwrap_or_default(),
        ..GenericsInfo::default()
    };
    for param in &generics.params {
        match param {
            GenericParam::Lifetime(l) => info.lifetimes.push(l.lifetime.to_string()),
            GenericParam::Type(t) => info.type_params.push(TypeParamInfo {
                name: t.ident.to_string(),
                bounds: t.bounds.iter().map(print).collect(),
                default: t.default.as_ref().map(print),
            }),
            GenericParam::Const(c) => info.const_params.push(ConstParamInfo {
                name: c.ident.to_string(),
                ty: type_info(&c.ty),
                default: c.default.as_ref().map(print),
            }),
        }
    }
    info
}

fn print_generic_params(generics: &Generics) -> String {
    let params = &generics.params;
    print_tokens(quote::quote!(< #params >))
}

/// Type or const parameters (lifetimes alone do not count).
pub fn has_type_generics(info: &GenericsInfo) -> bool {
    !info.type_params.is_empty() || !info.const_params.is_empty()
}

/// An unsigned integer literal (`3`, `3usize`, `0x10`, `1_000`) or `None`.
pub fn parse_unsigned(text: &str) -> Option<u64> {
    parse_integer(text).and_then(|v| if v >= 0 { Some(v as u64) } else { None })
}

/// An integer literal, optionally negated (`-1`, `- 1`, `0b101i8`), or `None`.
pub fn parse_integer(text: &str) -> Option<i64> {
    let compact: String = text.chars().filter(|c| !c.is_whitespace() && *c != '_').collect();
    let (negative, body) = match compact.strip_prefix('-') {
        Some(rest) => (true, rest.to_string()),
        None => (false, compact),
    };
    let (radix, digits) = if let Some(r) = body.strip_prefix("0x") {
        (16, r.to_string())
    } else if let Some(r) = body.strip_prefix("0b") {
        (2, r.to_string())
    } else if let Some(r) = body.strip_prefix("0o") {
        (8, r.to_string())
    } else {
        (10, body)
    };
    let mut end = digits.len();
    for suffix in ["usize", "isize", "u128", "i128", "u64", "i64", "u32", "i32", "u16", "i16", "u8", "i8"] {
        if digits.ends_with(suffix) {
            end = digits.len() - suffix.len();
            break;
        }
    }
    let digits = &digits[..end];
    if digits.is_empty() {
        return None;
    }
    let value = i128::from_str_radix(digits, radix).ok()?;
    let value = if negative { -value } else { value };
    i64::try_from(value).ok()
}
