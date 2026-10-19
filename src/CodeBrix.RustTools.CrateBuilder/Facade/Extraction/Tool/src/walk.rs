//! Walks the module tree from the crate root (`mod x;` -> x.rs | x/mod.rs | #[path]; inline
//! modules) and records every `pub` item, inherent-impl method, trait and trait impl, plus every
//! `use` declaration for the re-export resolution in resolve.rs.

use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};

use quote::quote;
use syn::spanned::Spanned;
use syn::{Attribute, Expr, Fields, FnArg, ImplItem, Item, Meta, Pat, TraitItem, Type, UseTree, Visibility};

use crate::model::{
    ConstantInfo, ConstantKind, Diagnostic, EnumInfo, FieldInfo, FunctionInfo, ItemHeader, OwnerInfo, ParamInfo,
    SelfKind, Severity, Shape, StructInfo, TraitImplInfo, TraitInfo, TypeAliasInfo, TypeInfo, VariantInfo,
};
use crate::types::{abi_name, generics_info, has_type_generics, parse_integer, print, print_tokens, return_type, type_info};

#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Debug)]
pub enum ItemKind {
    Function,
    Struct,
    Enum,
    Alias,
    Constant,
    Trait,
}

/// What a name refers to.
#[derive(Clone, PartialEq, Eq, PartialOrd, Ord, Hash, Debug)]
pub enum Target {
    Module(usize),
    Item(ItemKind, usize),
    External(String),
    /// A declaration that exists but is not part of the public API (not recorded).
    Dropped,
}

pub struct Decl {
    pub name: String,
    pub target: Target,
    pub is_pub: bool,
}

#[derive(Clone)]
pub enum UseLeaf {
    /// `use a::b;` / `use a::b as c;` / `use a::{self}`: the path names the target.
    Name(String),
    /// `use a::*;`
    Glob,
}

#[derive(Clone)]
pub struct UseDecl {
    pub is_pub: bool,
    pub visibility: String,
    pub path: Vec<String>,
    pub leaf: UseLeaf,
    pub leading_colon: bool,
    pub cfg: Vec<String>,
    pub file: String,
    pub line: u32,
}

pub struct ModNode {
    pub path: Vec<String>,
    pub parent: Option<usize>,
    pub visibility: String,
    pub file: String,
    pub inline: bool,
    pub cfg: Vec<String>,
    pub docs: String,
    pub decls: Vec<Decl>,
    pub uses: Vec<UseDecl>,
}

impl ModNode {
    pub fn path_text(&self) -> String {
        self.path.join("::")
    }
}

/// A method or associated const whose owner type is resolved after the walk.
pub struct PendingOwner {
    pub kind: ItemKind,
    pub index: usize,
    pub module: usize,
    pub self_ty: Type,
}

pub struct PendingImpl {
    pub index: usize,
    pub module: usize,
    pub self_ty: Type,
}

#[derive(Default)]
pub struct Collected {
    pub modules: Vec<ModNode>,
    pub functions: Vec<FunctionInfo>,
    pub structs: Vec<StructInfo>,
    pub enums: Vec<EnumInfo>,
    pub aliases: Vec<TypeAliasInfo>,
    pub constants: Vec<ConstantInfo>,
    pub traits: Vec<TraitInfo>,
    pub trait_impls: Vec<TraitImplInfo>,
    pub diagnostics: Vec<Diagnostic>,
    pub pending_owners: Vec<PendingOwner>,
    pub pending_impls: Vec<PendingImpl>,
    pub crate_docs: String,
}

struct Ctx {
    module: usize,
    /// The file being read, relative to the package folder.
    file: String,
    /// The folder of the file being read.
    file_dir: PathBuf,
    /// The folder where `mod x;` looks for x.rs / x/mod.rs.
    child_dir: PathBuf,
    /// Whether the items are inside an inline `mod x { }` block.
    in_inline: bool,
}

pub struct AttrInfo {
    pub docs: String,
    pub cfg: Vec<String>,
    pub derives: Vec<String>,
    pub repr: Vec<String>,
    pub others: Vec<String>,
    pub doc_hidden: bool,
    pub path: Option<String>,
}

pub fn attr_info(attrs: &[Attribute]) -> AttrInfo {
    let mut info = AttrInfo {
        docs: String::new(),
        cfg: Vec::new(),
        derives: Vec::new(),
        repr: Vec::new(),
        others: Vec::new(),
        doc_hidden: false,
        path: None,
    };
    let mut doc_lines: Vec<String> = Vec::new();
    for attr in attrs {
        let name = attr.path().segments.last().map(|s| s.ident.to_string()).unwrap_or_default();
        let single = attr.path().segments.len() == 1;
        match (&attr.meta, name.as_str()) {
            (Meta::NameValue(nv), "doc") if single => {
                if let Expr::Lit(lit) = &nv.value
                    && let syn::Lit::Str(s) = &lit.lit {
                        for line in s.value().split('\n') {
                            doc_lines.push(line.trim_end_matches('\r').to_string());
                        }
                        continue;
                    }
                info.others.push(format!("#[{}]", print(&attr.meta)));
            }
            (Meta::NameValue(nv), "path") if single => {
                if let Expr::Lit(lit) = &nv.value
                    && let syn::Lit::Str(s) = &lit.lit {
                        info.path = Some(s.value());
                    }
                info.others.push(format!("#[{}]", print(&attr.meta)));
            }
            (Meta::List(list), "cfg") if single => info.cfg.push(print_tokens(list.tokens.clone())),
            (Meta::List(list), "derive") if single => {
                let parsed = list.parse_args_with(syn::punctuated::Punctuated::<syn::Path, syn::Token![,]>::parse_terminated);
                match parsed {
                    Ok(paths) => info.derives.extend(paths.iter().map(print)),
                    Err(_) => info.others.push(format!("#[{}]", print(&attr.meta))),
                }
            }
            (Meta::List(list), "repr") if single => {
                let parsed = list.parse_args_with(syn::punctuated::Punctuated::<Meta, syn::Token![,]>::parse_terminated);
                match parsed {
                    Ok(metas) => info.repr.extend(metas.iter().map(print)),
                    Err(_) => info.repr.push(print_tokens(list.tokens.clone())),
                }
            }
            (Meta::List(list), "doc") if single => {
                let text = print_tokens(list.tokens.clone());
                if text.split(',').any(|p| p.trim() == "hidden") {
                    info.doc_hidden = true;
                }
                info.others.push(format!("#[{}]", print(&attr.meta)));
            }
            _ => {
                let prefix = if matches!(attr.style, syn::AttrStyle::Inner(_)) { "#![" } else { "#[" };
                info.others.push(format!("{}{}]", prefix, print(&attr.meta)));
            }
        }
    }
    info.docs = join_docs(&doc_lines);
    info
}

/// Whether a cfg predicate holds only in test builds: `test`, `doctest`, or `all(...)` with
/// one of those among its parts.
fn is_test_cfg(cfg: &str) -> bool {
    let compact: String = cfg.chars().filter(|c| !c.is_whitespace()).collect();
    if compact == "test" || compact == "doctest" {
        return true;
    }
    if let Some(inner) = compact.strip_prefix("all(").and_then(|r| r.strip_suffix(')')) {
        return inner.split(',').any(|part| part == "test" || part == "doctest");
    }
    false
}

/// Joins doc lines the way rustdoc does: the common leading indentation is removed and
/// leading / trailing blank lines are dropped.
fn join_docs(lines: &[String]) -> String {
    let indent = lines
        .iter()
        .filter(|l| !l.trim().is_empty())
        .map(|l| l.len() - l.trim_start().len())
        .min()
        .unwrap_or(0);
    let mut out: Vec<String> = lines
        .iter()
        .map(|l| if l.trim().is_empty() { String::new() } else { l[indent.min(l.len())..].to_string() })
        .collect();
    while out.first().map(|l| l.is_empty()).unwrap_or(false) {
        out.remove(0);
    }
    while out.last().map(|l| l.is_empty()).unwrap_or(false) {
        out.pop();
    }
    out.join("\n")
}

pub fn visibility(vis: &Visibility) -> (String, bool) {
    match vis {
        Visibility::Public(_) => ("pub".to_string(), true),
        Visibility::Restricted(r) => {
            let path = print(&r.path);
            if r.in_token.is_some() {
                (format!("pub(in {})", path), false)
            } else {
                (format!("pub({})", path), false)
            }
        }
        Visibility::Inherited => ("private".to_string(), false),
    }
}

fn line_of<T: Spanned>(node: &T) -> u32 {
    node.span().start().line as u32
}

impl Collected {
    /// Reads the crate root and everything below it.
    pub fn walk_crate(package_dir: &Path, root_file: &Path) -> Collected {
        let mut c = Collected::default();
        let rel = relative(package_dir, root_file);
        c.modules.push(ModNode {
            path: vec!["crate".to_string()],
            parent: None,
            visibility: "pub".to_string(),
            file: rel.clone(),
            inline: false,
            cfg: Vec::new(),
            docs: String::new(),
            decls: Vec::new(),
            uses: Vec::new(),
        });
        let dir = root_file.parent().map(Path::to_path_buf).unwrap_or_default();
        if let Some(file) = c.parse_file(package_dir, root_file) {
            let attrs = attr_info(&file.attrs);
            c.crate_docs = attrs.docs.clone();
            c.modules[0].docs = attrs.docs;
            c.modules[0].cfg = attrs.cfg;
            let ctx = Ctx { module: 0, file: rel, file_dir: dir.clone(), child_dir: dir, in_inline: false };
            c.walk_items(package_dir, &file.items, &ctx);
        }
        c
    }

    fn parse_file(&mut self, package_dir: &Path, path: &Path) -> Option<syn::File> {
        let rel = relative(package_dir, path);
        let text = match fs::read_to_string(path) {
            Ok(t) => t,
            Err(e) => {
                self.diagnostics.push(Diagnostic {
                    severity: Severity::Error,
                    code: "file_unreadable".to_string(),
                    message: format!("{}: {}", rel, e),
                    file: Some(rel),
                    line: None,
                    column: None,
                });
                return None;
            }
        };
        match syn::parse_file(&text) {
            Ok(f) => Some(f),
            Err(e) => {
                let start = e.span().start();
                self.diagnostics.push(Diagnostic {
                    severity: Severity::Error,
                    code: "parse_error".to_string(),
                    message: format!("{} could not be parsed: {}", rel, e),
                    file: Some(rel),
                    line: Some(start.line as u32),
                    column: Some(start.column as u32 + 1),
                });
                None
            }
        }
    }

    fn header(&self, ctx: &Ctx, name: String, vis: &str, attrs: &AttrInfo, line: u32) -> ItemHeader {
        let module = &self.modules[ctx.module];
        let mut cfg = module.cfg.clone();
        cfg.extend(attrs.cfg.iter().cloned());
        ItemHeader {
            id: String::new(),
            canonical_path: format!("{}::{}", module.path_text(), name),
            name,
            public_paths: Vec::new(),
            module: module.path_text(),
            visibility: vis.to_string(),
            cfg,
            docs: attrs.docs.clone(),
            is_doc_hidden: attrs.doc_hidden,
            attributes: attrs.others.clone(),
            file: ctx.file.clone(),
            line,
        }
    }

    fn declare(&mut self, module: usize, name: String, target: Target, is_pub: bool) {
        self.modules[module].decls.push(Decl { name, target, is_pub });
    }

    fn walk_items(&mut self, package_dir: &Path, items: &[Item], ctx: &Ctx) {
        for item in items {
            match item {
                Item::Fn(f) => {
                    let (vis, is_pub) = visibility(&f.vis);
                    let name = f.sig.ident.to_string();
                    if is_pub {
                        let attrs = attr_info(&f.attrs);
                        let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&f.sig.ident));
                        let func = function_info(header, &f.vis, &f.sig, None, None);
                        self.functions.push(func);
                        let index = self.functions.len() - 1;
                        self.declare(ctx.module, name, Target::Item(ItemKind::Function, index), true);
                    } else {
                        self.declare(ctx.module, name, Target::Dropped, false);
                    }
                }
                Item::Struct(s) => {
                    let (vis, is_pub) = visibility(&s.vis);
                    let name = s.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&s.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&s.ident));
                    let (shape, fields) = fields_info(&s.fields);
                    let generics = generics_info(&s.generics);
                    let tuple_arity = if shape == Shape::Tuple { fields.len() as u32 } else { 0 };
                    self.structs.push(StructInfo {
                        header,
                        shape,
                        fields,
                        tuple_arity,
                        derives: attrs.derives.clone(),
                        repr: attrs.repr.clone(),
                        has_generics: has_type_generics(&generics),
                        generics,
                        implemented_traits: Vec::new(),
                    });
                    let index = self.structs.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Struct, index), true);
                }
                Item::Union(u) => {
                    let (vis, is_pub) = visibility(&u.vis);
                    let name = u.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&u.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&u.ident));
                    let (_, fields) = named_fields(u.fields.named.iter());
                    let generics = generics_info(&u.generics);
                    self.structs.push(StructInfo {
                        header,
                        shape: Shape::Union,
                        fields,
                        tuple_arity: 0,
                        derives: attrs.derives.clone(),
                        repr: attrs.repr.clone(),
                        has_generics: has_type_generics(&generics),
                        generics,
                        implemented_traits: Vec::new(),
                    });
                    let index = self.structs.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Struct, index), true);
                }
                Item::Enum(e) => {
                    let (vis, is_pub) = visibility(&e.vis);
                    let name = e.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&e.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&e.ident));
                    let variants: Vec<VariantInfo> = e
                        .variants
                        .iter()
                        .map(|v| {
                            let vattrs = attr_info(&v.attrs);
                            let (shape, fields) = fields_info(&v.fields);
                            let discriminant = v.discriminant.as_ref().map(|(_, expr)| print(expr));
                            VariantInfo {
                                name: v.ident.to_string(),
                                shape,
                                fields,
                                discriminant_value: discriminant.as_deref().and_then(parse_integer),
                                discriminant,
                                cfg: vattrs.cfg,
                                docs: vattrs.docs,
                                attributes: vattrs.others,
                            }
                        })
                        .collect();
                    let generics = generics_info(&e.generics);
                    self.enums.push(EnumInfo {
                        header,
                        is_c_like: variants.iter().all(|v| v.shape == Shape::Unit),
                        variants,
                        derives: attrs.derives.clone(),
                        repr: attrs.repr.clone(),
                        has_generics: has_type_generics(&generics),
                        generics,
                        implemented_traits: Vec::new(),
                    });
                    let index = self.enums.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Enum, index), true);
                }
                Item::Type(t) => {
                    let (vis, is_pub) = visibility(&t.vis);
                    let name = t.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&t.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&t.ident));
                    let generics = generics_info(&t.generics);
                    self.aliases.push(TypeAliasInfo { header, has_generics: has_type_generics(&generics), generics, ty: type_info(&t.ty) });
                    let index = self.aliases.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Alias, index), true);
                }
                Item::Const(k) => {
                    let (vis, is_pub) = visibility(&k.vis);
                    let name = k.ident.to_string();
                    if name == "_" {
                        continue;
                    }
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&k.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&k.ident));
                    self.constants.push(ConstantInfo {
                        header,
                        kind: ConstantKind::Const,
                        is_mutable: false,
                        ty: type_info(&k.ty),
                        value: literal_value(&k.expr),
                        owner: None,
                    });
                    let index = self.constants.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Constant, index), true);
                }
                Item::Static(s) => {
                    let (vis, is_pub) = visibility(&s.vis);
                    let name = s.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&s.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&s.ident));
                    self.constants.push(ConstantInfo {
                        header,
                        kind: ConstantKind::Static,
                        is_mutable: matches!(s.mutability, syn::StaticMutability::Mut(_)),
                        ty: type_info(&s.ty),
                        value: literal_value(&s.expr),
                        owner: None,
                    });
                    let index = self.constants.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Constant, index), true);
                }
                Item::Trait(t) => {
                    let (vis, is_pub) = visibility(&t.vis);
                    let name = t.ident.to_string();
                    if !is_pub {
                        self.declare(ctx.module, name, Target::Dropped, false);
                        continue;
                    }
                    let attrs = attr_info(&t.attrs);
                    let header = self.header(ctx, name.clone(), &vis, &attrs, line_of(&t.ident));
                    let mut methods = Vec::new();
                    let mut associated_types = Vec::new();
                    let mut associated_consts = Vec::new();
                    for trait_item in &t.items {
                        match trait_item {
                            TraitItem::Fn(m) => {
                                let mattrs = attr_info(&m.attrs);
                                let mut mheader = self.header(ctx, m.sig.ident.to_string(), "pub", &mattrs, line_of(&m.sig.ident));
                                mheader.canonical_path = format!("{}::{}", header.canonical_path, m.sig.ident);
                                mheader.cfg = header.cfg.iter().cloned().chain(mattrs.cfg.iter().cloned()).collect();
                                let mut func = function_info(mheader, &Visibility::Inherited, &m.sig, None, None);
                                func.has_default = Some(m.default.is_some());
                                methods.push(func);
                            }
                            TraitItem::Type(ty) => associated_types.push(ty.ident.to_string()),
                            TraitItem::Const(c) => associated_consts.push(c.ident.to_string()),
                            _ => {}
                        }
                    }
                    let generics = generics_info(&t.generics);
                    self.traits.push(TraitInfo {
                        header,
                        has_generics: has_type_generics(&generics),
                        generics,
                        supertraits: t.supertraits.iter().map(print).collect(),
                        is_unsafe: t.unsafety.is_some(),
                        is_auto: t.auto_token.is_some(),
                        methods,
                        associated_types,
                        associated_consts,
                    });
                    let index = self.traits.len() - 1;
                    self.declare(ctx.module, name, Target::Item(ItemKind::Trait, index), true);
                }
                Item::Impl(imp) => self.walk_impl(ctx, imp),
                Item::Mod(m) => self.walk_mod(package_dir, ctx, m),
                Item::Use(u) => {
                    let (vis, is_pub) = visibility(&u.vis);
                    let attrs = attr_info(&u.attrs);
                    let mut cfg = self.modules[ctx.module].cfg.clone();
                    cfg.extend(attrs.cfg);
                    let line = line_of(u);
                    let mut leaves = Vec::new();
                    flatten_use(&u.tree, &mut Vec::new(), &mut leaves);
                    for (path, leaf) in leaves {
                        self.modules[ctx.module].uses.push(UseDecl {
                            is_pub,
                            visibility: vis.clone(),
                            path,
                            leaf,
                            leading_colon: u.leading_colon.is_some(),
                            cfg: cfg.clone(),
                            file: ctx.file.clone(),
                            line,
                        });
                    }
                }
                Item::ExternCrate(x) => {
                    let (_, is_pub) = visibility(&x.vis);
                    let name = x.rename.as_ref().map(|(_, i)| i.to_string()).unwrap_or_else(|| x.ident.to_string());
                    self.declare(ctx.module, name, Target::External(x.ident.to_string()), is_pub);
                }
                Item::Macro(m) => {
                    // A macro in test-only code (`#[cfg(test)] mod tests`, or the macro itself
                    // under #[cfg(test)]) never reaches the library, so it is not reported.
                    let test_only = self.modules[ctx.module].cfg.iter().chain(attr_info(&m.attrs).cfg.iter()).any(|c| is_test_cfg(c));
                    if m.ident.is_none() && !test_only {
                        self.diagnostics.push(Diagnostic {
                            severity: Severity::Info,
                            code: "item_macro_not_expanded".to_string(),
                            message: format!(
                                "{}: the item macro `{}!` is not expanded; any items it generates are not part of this document",
                                self.modules[ctx.module].path_text(),
                                print(&m.mac.path)
                            ),
                            file: Some(ctx.file.clone()),
                            line: Some(line_of(m)),
                            column: None,
                        });
                    }
                }
                Item::Verbatim(_) => {
                    self.diagnostics.push(Diagnostic {
                        severity: Severity::Warning,
                        code: "unsupported_item".to_string(),
                        message: format!("{}: an item syn does not model was skipped", self.modules[ctx.module].path_text()),
                        file: Some(ctx.file.clone()),
                        line: Some(line_of(item)),
                        column: None,
                    });
                }
                _ => {}
            }
        }
    }

    fn walk_impl(&mut self, ctx: &Ctx, imp: &syn::ItemImpl) {
        let impl_attrs = attr_info(&imp.attrs);
        let mut impl_cfg = self.modules[ctx.module].cfg.clone();
        impl_cfg.extend(impl_attrs.cfg.iter().cloned());
        let impl_generics = generics_info(&imp.generics);
        let module_path = self.modules[ctx.module].path_text();
        if let Some((negation, trait_path, _)) = &imp.trait_ {
            let methods = imp
                .items
                .iter()
                .filter_map(|i| if let ImplItem::Fn(f) = i { Some(f.sig.ident.to_string()) } else { None })
                .collect();
            let associated_types = imp
                .items
                .iter()
                .filter_map(|i| if let ImplItem::Type(t) = i { Some(t.ident.to_string()) } else { None })
                .collect();
            self.trait_impls.push(TraitImplInfo {
                module: module_path,
                file: ctx.file.clone(),
                line: line_of(&imp.self_ty),
                cfg: impl_cfg,
                trait_path: print(trait_path),
                trait_name: trait_path.segments.last().map(|s| s.ident.to_string()).unwrap_or_default(),
                self_type: type_info(&imp.self_ty),
                self_path: None,
                generics: impl_generics,
                is_unsafe: imp.unsafety.is_some(),
                is_negative: negation.is_some(),
                methods,
                associated_types,
            });
            self.pending_impls.push(PendingImpl { index: self.trait_impls.len() - 1, module: ctx.module, self_ty: (*imp.self_ty).clone() });
            return;
        }
        let owner = OwnerInfo {
            type_text: print(&imp.self_ty),
            ty: type_info(&imp.self_ty),
            resolved_path: None,
            generics: impl_generics.clone(),
        };
        for impl_item in &imp.items {
            match impl_item {
                ImplItem::Fn(f) => {
                    let (vis, is_pub) = visibility(&f.vis);
                    if !is_pub {
                        continue;
                    }
                    let attrs = attr_info(&f.attrs);
                    let mut header = self.header(ctx, f.sig.ident.to_string(), &vis, &attrs, line_of(&f.sig.ident));
                    header.cfg = impl_cfg.iter().cloned().chain(attrs.cfg.iter().cloned()).collect();
                    let func = function_info(header, &f.vis, &f.sig, Some(owner.clone()), Some(&impl_generics));
                    self.functions.push(func);
                    self.pending_owners.push(PendingOwner {
                        kind: ItemKind::Function,
                        index: self.functions.len() - 1,
                        module: ctx.module,
                        self_ty: (*imp.self_ty).clone(),
                    });
                }
                ImplItem::Const(k) => {
                    let (vis, is_pub) = visibility(&k.vis);
                    if !is_pub {
                        continue;
                    }
                    let attrs = attr_info(&k.attrs);
                    let mut header = self.header(ctx, k.ident.to_string(), &vis, &attrs, line_of(&k.ident));
                    header.cfg = impl_cfg.iter().cloned().chain(attrs.cfg.iter().cloned()).collect();
                    self.constants.push(ConstantInfo {
                        header,
                        kind: ConstantKind::Const,
                        is_mutable: false,
                        ty: type_info(&k.ty),
                        value: literal_value(&k.expr),
                        owner: Some(owner.clone()),
                    });
                    self.pending_owners.push(PendingOwner {
                        kind: ItemKind::Constant,
                        index: self.constants.len() - 1,
                        module: ctx.module,
                        self_ty: (*imp.self_ty).clone(),
                    });
                }
                _ => {}
            }
        }
    }

    fn walk_mod(&mut self, package_dir: &Path, ctx: &Ctx, m: &syn::ItemMod) {
        let (vis, is_pub) = visibility(&m.vis);
        let attrs = attr_info(&m.attrs);
        let name = m.ident.to_string();
        let parent_path = self.modules[ctx.module].path.clone();
        let mut cfg = self.modules[ctx.module].cfg.clone();
        cfg.extend(attrs.cfg.iter().cloned());
        let mut path = parent_path;
        path.push(name.clone());
        let index = self.modules.len();
        self.modules.push(ModNode {
            path,
            parent: Some(ctx.module),
            visibility: vis,
            file: ctx.file.clone(),
            inline: m.content.is_some(),
            cfg,
            docs: attrs.docs.clone(),
            decls: Vec::new(),
            uses: Vec::new(),
        });
        self.declare(ctx.module, name.clone(), Target::Module(index), is_pub);

        if let Some((_, items)) = &m.content {
            // Inline module: children live in <child_dir>/<name>/; #[path] inside it is
            // relative to that folder.
            let child_dir = match &attrs.path {
                Some(p) => ctx.child_dir.join(p),
                None => ctx.child_dir.join(&name),
            };
            let inner = Ctx { module: index, file: ctx.file.clone(), file_dir: ctx.file_dir.clone(), child_dir, in_inline: true };
            self.walk_items(package_dir, items, &inner);
            return;
        }

        let candidates: Vec<PathBuf> = match &attrs.path {
            Some(p) if ctx.in_inline => vec![ctx.child_dir.join(p)],
            Some(p) => vec![ctx.file_dir.join(p)],
            None => vec![ctx.child_dir.join(format!("{}.rs", name)), ctx.child_dir.join(&name).join("mod.rs")],
        };
        let Some(file_path) = candidates.iter().find(|p| p.is_file()).cloned() else {
            let module_path = self.modules[index].path_text();
            self.diagnostics.push(Diagnostic {
                severity: Severity::Warning,
                code: "module_file_not_found".to_string(),
                message: format!(
                    "the file of module {} was not found (looked for {})",
                    module_path,
                    candidates.iter().map(|c| relative(package_dir, c)).collect::<Vec<_>>().join(", ")
                ),
                file: Some(ctx.file.clone()),
                line: Some(line_of(&m.ident)),
                column: None,
            });
            return;
        };
        let rel = relative(package_dir, &file_path);
        self.modules[index].file = rel.clone();
        let Some(file) = self.parse_file(package_dir, &file_path) else { return };
        let inner_attrs = attr_info(&file.attrs);
        if !inner_attrs.docs.is_empty() {
            let docs = &mut self.modules[index].docs;
            if docs.is_empty() {
                *docs = inner_attrs.docs.clone();
            } else {
                docs.push('\n');
                docs.push_str(&inner_attrs.docs);
            }
        }
        self.modules[index].cfg.extend(inner_attrs.cfg.iter().cloned());
        let file_dir = file_path.parent().map(Path::to_path_buf).unwrap_or_default();
        let is_mod_rs = attrs.path.is_some() || file_path.file_name().map(|f| f == "mod.rs").unwrap_or(false);
        let child_dir = if is_mod_rs { file_dir.clone() } else { file_dir.join(&name) };
        let inner = Ctx { module: index, file: rel, file_dir, child_dir, in_inline: false };
        self.walk_items(package_dir, &file.items, &inner);
    }
}

fn flatten_use(tree: &UseTree, prefix: &mut Vec<String>, out: &mut Vec<(Vec<String>, UseLeaf)>) {
    match tree {
        UseTree::Path(p) => {
            prefix.push(p.ident.to_string());
            flatten_use(&p.tree, prefix, out);
            prefix.pop();
        }
        UseTree::Name(n) => {
            let ident = n.ident.to_string();
            if ident == "self" {
                if let Some(last) = prefix.last().cloned() {
                    out.push((prefix.clone(), UseLeaf::Name(last)));
                }
            } else {
                let mut path = prefix.clone();
                path.push(ident.clone());
                out.push((path, UseLeaf::Name(ident)));
            }
        }
        UseTree::Rename(r) => {
            let rename = r.rename.to_string();
            if rename == "_" {
                return;
            }
            let mut path = prefix.clone();
            if r.ident != "self" {
                path.push(r.ident.to_string());
            }
            out.push((path, UseLeaf::Name(rename)));
        }
        UseTree::Glob(_) => out.push((prefix.clone(), UseLeaf::Glob)),
        UseTree::Group(g) => {
            for t in &g.items {
                flatten_use(t, prefix, out);
            }
        }
    }
}

fn fields_info(fields: &Fields) -> (Shape, Vec<FieldInfo>) {
    match fields {
        Fields::Named(n) => named_fields(n.named.iter()),
        Fields::Unnamed(u) => (Shape::Tuple, u.unnamed.iter().enumerate().map(|(i, f)| field_info(f, i)).collect()),
        Fields::Unit => (Shape::Unit, Vec::new()),
    }
}

fn named_fields<'a>(fields: impl Iterator<Item = &'a syn::Field>) -> (Shape, Vec<FieldInfo>) {
    (Shape::Named, fields.enumerate().map(|(i, f)| field_info(f, i)).collect())
}

fn field_info(field: &syn::Field, index: usize) -> FieldInfo {
    let attrs = attr_info(&field.attrs);
    FieldInfo {
        name: field.ident.as_ref().map(|i| i.to_string()),
        index: index as u32,
        ty: type_info(&field.ty),
        visibility: visibility(&field.vis).0,
        cfg: attrs.cfg,
        docs: attrs.docs,
        attributes: attrs.others,
    }
}

fn literal_value(expr: &Expr) -> Option<String> {
    match expr {
        Expr::Lit(_) => Some(print(expr)),
        Expr::Unary(u) if matches!(u.op, syn::UnOp::Neg(_)) && matches!(*u.expr, Expr::Lit(_)) => Some(print(expr)),
        Expr::Group(g) => literal_value(&g.expr),
        _ => None,
    }
}

fn function_info(
    header: ItemHeader,
    vis: &Visibility,
    sig: &syn::Signature,
    owner: Option<OwnerInfo>,
    impl_generics: Option<&crate::model::GenericsInfo>,
) -> FunctionInfo {
    let mut self_kind = SelfKind::None;
    let mut self_type: Option<TypeInfo> = None;
    let mut params = Vec::new();
    for (i, input) in sig.inputs.iter().enumerate() {
        match input {
            FnArg::Receiver(r) => {
                if r.colon_token.is_some() {
                    self_kind = SelfKind::Typed;
                    self_type = Some(type_info(&r.ty));
                } else if r.reference.is_some() {
                    self_kind = if r.mutability.is_some() { SelfKind::RefMut } else { SelfKind::Ref };
                } else {
                    self_kind = if r.mutability.is_some() { SelfKind::MutValue } else { SelfKind::Value };
                }
            }
            FnArg::Typed(t) => {
                let (name, pattern) = match &*t.pat {
                    Pat::Ident(id) => (id.ident.to_string(), None),
                    other => (format!("arg{}", i), Some(print(other))),
                };
                params.push(ParamInfo { name, pattern, ty: type_info(&t.ty) });
            }
        }
    }
    let generics = generics_info(&sig.generics);
    let has_generics = has_type_generics(&generics) || impl_generics.map(has_type_generics).unwrap_or(false);
    let signature = print_tokens(quote!(#vis #sig));
    FunctionInfo {
        header,
        owner,
        self_kind,
        self_type,
        params,
        return_type: return_type(&sig.output),
        generics,
        has_generics,
        is_unsafe: sig.unsafety.is_some(),
        is_async: sig.asyncness.is_some(),
        is_const: sig.constness.is_some(),
        abi: sig.abi.as_ref().map(abi_name),
        signature,
        has_default: None,
    }
}

pub fn relative(base: &Path, path: &Path) -> String {
    let rel = path.strip_prefix(base).unwrap_or(path);
    let mut parts: Vec<String> = Vec::new();
    for component in rel.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                if parts.last().map(|p| p != "..").unwrap_or(false) {
                    parts.pop();
                } else {
                    parts.push("..".to_string());
                }
            }
            other => parts.push(other.as_os_str().to_string_lossy().to_string()),
        }
    }
    parts.join("/")
}

/// Assigns `id` (`<kind>:<canonical path>`, `#2`, `#3` ... for repeats) to every item.
pub fn assign_ids(c: &mut Collected) {
    let mut seen: HashMap<String, usize> = HashMap::new();
    let mut next = |kind: &str, path: &str| -> String {
        let base = format!("{}:{}", kind, path);
        let count = seen.entry(base.clone()).or_insert(0);
        *count += 1;
        if *count == 1 { base } else { format!("{}#{}", base, count) }
    };
    for f in &mut c.functions {
        f.header.id = next("fn", &f.header.canonical_path);
    }
    for s in &mut c.structs {
        let kind = if s.shape == Shape::Union { "union" } else { "struct" };
        s.header.id = next(kind, &s.header.canonical_path);
    }
    for e in &mut c.enums {
        e.header.id = next("enum", &e.header.canonical_path);
    }
    for a in &mut c.aliases {
        a.header.id = next("type", &a.header.canonical_path);
    }
    for k in &mut c.constants {
        let kind = if k.kind == ConstantKind::Static { "static" } else { "const" };
        k.header.id = next(kind, &k.header.canonical_path);
    }
    for t in &mut c.traits {
        t.header.id = next("trait", &t.header.canonical_path);
        let trait_path = t.header.canonical_path.clone();
        for m in &mut t.methods {
            m.header.id = format!("fn:{}::{}", trait_path, m.header.name);
        }
    }
}
