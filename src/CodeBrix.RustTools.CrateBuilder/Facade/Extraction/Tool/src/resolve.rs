//! Name resolution for `use` paths and the public paths of every item: the paths through
//! `pub mod`s and `pub use` re-exports (named and glob) by which code outside the crate can
//! name it. Resolution is syntactic: names that are not found inside the crate are taken to be
//! external (another crate or std).

use std::collections::{BTreeMap, BTreeSet, HashMap};

use syn::Type;

use crate::model::{Diagnostic, ReexportInfo, Severity, TypeInfo, TypeKind};
use crate::walk::{Collected, ItemKind, ModNode, Target, UseDecl, UseLeaf};

const MAX_DEPTH: usize = 12;
const MAX_EXPANSIONS_PER_MODULE: usize = 4;
const MAX_PATH_SEGMENTS: usize = 12;

struct Resolver<'a> {
    modules: &'a [ModNode],
}

impl<'a> Resolver<'a> {
    /// Everything `name` can refer to inside module `m` (declarations, imports, glob imports).
    fn lookup_scope(&self, m: usize, name: &str, depth: usize) -> Vec<Target> {
        if depth > MAX_DEPTH {
            return Vec::new();
        }
        let module = &self.modules[m];
        let mut out: Vec<Target> = module.decls.iter().filter(|d| d.name == name).map(|d| d.target.clone()).collect();
        for u in &module.uses {
            if let UseLeaf::Name(exported) = &u.leaf
                && exported == name {
                    // `use foo;` naming an external crate would otherwise find itself.
                    if u.path.len() == 1 && u.path[0] == name && !u.leading_colon {
                        out.push(Target::External(name.to_string()));
                        continue;
                    }
                    out.extend(self.resolve_use_path(m, u, depth + 1));
                }
        }
        if out.is_empty() {
            for u in &module.uses {
                if let UseLeaf::Glob = u.leaf {
                    for t in self.resolve_use_path(m, u, depth + 1) {
                        if let Target::Module(k) = t
                            && k != m {
                                out.extend(self.lookup_scope(k, name, depth + 1));
                            }
                    }
                }
            }
        }
        dedupe(out)
    }

    fn resolve_use_path(&self, m: usize, u: &UseDecl, depth: usize) -> Vec<Target> {
        if u.leading_colon {
            return vec![Target::External(u.path.join("::"))];
        }
        self.resolve_path(m, &u.path, depth)
    }

    /// Resolves a path written in module `m`.
    fn resolve_path(&self, m: usize, segs: &[String], depth: usize) -> Vec<Target> {
        if depth > MAX_DEPTH {
            return Vec::new();
        }
        let Some(first) = segs.first() else { return vec![Target::Module(m)] };
        let mut current: Vec<Target> = match first.as_str() {
            "crate" | "$crate" => vec![Target::Module(0)],
            "self" => vec![Target::Module(m)],
            "super" => self.modules[m].parent.map(|p| vec![Target::Module(p)]).unwrap_or_default(),
            name => {
                let found = self.lookup_scope(m, name, depth + 1);
                if found.is_empty() {
                    return vec![Target::External(segs.join("::"))];
                }
                found
            }
        };
        for seg in &segs[1..] {
            let mut next = Vec::new();
            for t in &current {
                match t {
                    Target::Module(j) => match seg.as_str() {
                        "super" => next.extend(self.modules[*j].parent.map(Target::Module)),
                        "self" => next.push(Target::Module(*j)),
                        name => next.extend(self.lookup_scope(*j, name, depth + 1)),
                    },
                    Target::External(p) => next.push(Target::External(format!("{}::{}", p, seg))),
                    _ => {}
                }
            }
            current = dedupe(next);
        }
        current
    }
}

/// Fills `TypeInfo::resolved` for every path type in a signature, field, alias or constant
/// that names exactly one struct, enum or type alias of this crate, resolving the path in the
/// module where it was written (so `Frame` in a module that does `use crate::codec::Frame`
/// is that `Frame`, not another item of the same name elsewhere in the crate).
fn annotate_types(c: &mut Collected) {
    let module_index: HashMap<String, usize> = c.modules.iter().enumerate().map(|(i, m)| (m.path_text(), i)).collect();
    let canonical: HashMap<(ItemKind, usize), String> = c
        .structs
        .iter()
        .enumerate()
        .map(|(i, s)| ((ItemKind::Struct, i), s.header.canonical_path.clone()))
        .chain(c.enums.iter().enumerate().map(|(i, e)| ((ItemKind::Enum, i), e.header.canonical_path.clone())))
        .chain(c.aliases.iter().enumerate().map(|(i, a)| ((ItemKind::Alias, i), a.header.canonical_path.clone())))
        .collect();
    let r = Resolver { modules: &c.modules };
    let annotate_in = |module: &str, ty: &mut TypeInfo| {
        if let Some(&m) = module_index.get(module) {
            annotate(&r, &canonical, m, ty);
        }
    };
    for f in c.functions.iter_mut() {
        let module = f.header.module.clone();
        for p in f.params.iter_mut() {
            annotate_in(&module, &mut p.ty);
        }
        annotate_in(&module, &mut f.return_type);
        if let Some(t) = f.self_type.as_mut() {
            annotate_in(&module, t);
        }
    }
    for s in c.structs.iter_mut() {
        let module = s.header.module.clone();
        for field in s.fields.iter_mut() {
            annotate_in(&module, &mut field.ty);
        }
    }
    for e in c.enums.iter_mut() {
        let module = e.header.module.clone();
        for v in e.variants.iter_mut() {
            for field in v.fields.iter_mut() {
                annotate_in(&module, &mut field.ty);
            }
        }
    }
    for a in c.aliases.iter_mut() {
        let module = a.header.module.clone();
        annotate_in(&module, &mut a.ty);
    }
    for k in c.constants.iter_mut() {
        let module = k.header.module.clone();
        annotate_in(&module, &mut k.ty);
    }
}

fn annotate(r: &Resolver, canonical: &HashMap<(ItemKind, usize), String>, m: usize, ty: &mut TypeInfo) {
    if matches!(ty.kind, TypeKind::Path) && ty.qself.is_none()
        && let Some(segments) = ty.segments.as_ref() {
            let names: Vec<String> = segments.iter().map(|s| s.name.clone()).collect();
            if !names.is_empty() && names[0] != "Self" {
                let targets = r.resolve_path(m, &names, 0);
                let items: Vec<&String> = targets
                    .iter()
                    .filter_map(|t| match t {
                        Target::Item(kind, i) => canonical.get(&(*kind, *i)),
                        _ => None,
                    })
                    .collect();
                if items.len() == 1 && targets.len() == 1 {
                    ty.resolved = Some(items[0].clone());
                } else if !matches!(names[0].as_str(), "crate" | "self" | "super" | "$crate") {
                    let first = r.lookup_scope(m, &names[0], 0);
                    if first.is_empty() {
                        ty.unresolved = Some(true);
                    } else if first.iter().all(|t| matches!(t, Target::External(_))) {
                        ty.external = Some(true);
                    }
                }
            }
        }
    for child in [ty.inner.as_deref_mut(), ty.elem.as_deref_mut(), ty.qself.as_deref_mut(), ty.output.as_deref_mut()].into_iter().flatten() {
        annotate(r, canonical, m, child);
    }
    for list in [ty.elems.as_mut(), ty.inputs.as_mut()].into_iter().flatten() {
        for child in list.iter_mut() {
            annotate(r, canonical, m, child);
        }
    }
    if let Some(segments) = ty.segments.as_mut() {
        for seg in segments.iter_mut() {
            for child in seg.args.iter_mut() {
                annotate(r, canonical, m, child);
            }
            for binding in seg.bindings.iter_mut() {
                annotate(r, canonical, m, &mut binding.ty);
            }
            if let Some(inputs) = seg.fn_inputs.as_mut() {
                for child in inputs.iter_mut() {
                    annotate(r, canonical, m, child);
                }
            }
            if let Some(out) = seg.fn_output.as_deref_mut() {
                annotate(r, canonical, m, out);
            }
        }
    }
}

fn dedupe(items: Vec<Target>) -> Vec<Target> {
    let mut seen = BTreeSet::new();
    items.into_iter().filter(|t| seen.insert(t.clone())).collect()
}

fn item_path(c: &Collected, kind: ItemKind, index: usize) -> String {
    match kind {
        ItemKind::Function => c.functions[index].header.canonical_path.clone(),
        ItemKind::Struct => c.structs[index].header.canonical_path.clone(),
        ItemKind::Enum => c.enums[index].header.canonical_path.clone(),
        ItemKind::Alias => c.aliases[index].header.canonical_path.clone(),
        ItemKind::Constant => c.constants[index].header.canonical_path.clone(),
        ItemKind::Trait => c.traits[index].header.canonical_path.clone(),
    }
}

fn target_text(c: &Collected, t: &Target) -> Option<String> {
    match t {
        Target::Module(j) => Some(c.modules[*j].path_text()),
        Target::Item(kind, i) => Some(item_path(c, *kind, *i)),
        Target::External(p) => Some(p.clone()),
        Target::Dropped => None,
    }
}

fn type_path_segments(ty: &Type) -> Option<Vec<String>> {
    match ty {
        Type::Path(p) if p.qself.is_none() => Some(p.path.segments.iter().map(|s| s.ident.to_string()).collect()),
        Type::Paren(p) => type_path_segments(&p.elem),
        Type::Group(g) => type_path_segments(&g.elem),
        _ => None,
    }
}

fn sort_paths(paths: &mut Vec<String>) {
    paths.sort_by(|a, b| a.matches("::").count().cmp(&b.matches("::").count()).then_with(|| a.cmp(b)));
    paths.dedup();
}

/// Fills owners, public paths, implemented traits and the re-export list.
pub fn resolve(c: &mut Collected) -> (Vec<ReexportInfo>, Vec<Vec<String>>) {
    let module_count = c.modules.len();
    let mut use_results: HashMap<(usize, usize), Vec<Target>> = HashMap::new();
    let mut exports: Vec<BTreeMap<String, BTreeSet<Target>>> = vec![BTreeMap::new(); module_count];
    let mut owner_targets: Vec<Option<Target>> = Vec::new();
    let mut impl_targets: Vec<Option<Target>> = Vec::new();
    {
        let r = Resolver { modules: &c.modules };
        for (m, module) in c.modules.iter().enumerate() {
            for d in &module.decls {
                if d.is_pub && d.target != Target::Dropped {
                    exports[m].entry(d.name.clone()).or_default().insert(d.target.clone());
                }
            }
            for (ui, u) in module.uses.iter().enumerate() {
                if u.is_pub {
                    use_results.insert((m, ui), r.resolve_use_path(m, u, 0));
                }
            }
        }
        // Fixed point: glob re-exports pull in what the target module exports.
        for _ in 0..32 {
            let mut changed = false;
            for (m, module) in c.modules.iter().enumerate() {
                for (ui, u) in module.uses.iter().enumerate() {
                    if !u.is_pub {
                        continue;
                    }
                    let targets = use_results.get(&(m, ui)).cloned().unwrap_or_default();
                    match &u.leaf {
                        UseLeaf::Name(name) => {
                            for t in targets {
                                if t != Target::Dropped && exports[m].entry(name.clone()).or_default().insert(t) {
                                    changed = true;
                                }
                            }
                        }
                        UseLeaf::Glob => {
                            for t in targets {
                                if let Target::Module(k) = t {
                                    if k == m {
                                        continue;
                                    }
                                    let source = exports[k].clone();
                                    for (name, ts) in source {
                                        for t in ts {
                                            if exports[m].entry(name.clone()).or_default().insert(t) {
                                                changed = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if !changed {
                break;
            }
        }
        for p in &c.pending_owners {
            owner_targets.push(resolve_type_owner(&r, p.module, &p.self_ty));
        }
        for p in &c.pending_impls {
            impl_targets.push(resolve_type_owner(&r, p.module, &p.self_ty));
        }
    }

    annotate_types(c);

    // Public paths: walk the exports from the crate root.
    let mut module_paths: Vec<Vec<String>> = vec![Vec::new(); module_count];
    let mut item_paths: HashMap<(ItemKind, usize), Vec<String>> = HashMap::new();
    let mut expansions = vec![0usize; module_count];
    module_paths[0].push("crate".to_string());
    let mut queue: std::collections::VecDeque<(usize, String)> = std::collections::VecDeque::new();
    queue.push_back((0, "crate".to_string()));
    expansions[0] = 1;
    while let Some((m, prefix)) = queue.pop_front() {
        if prefix.matches("::").count() + 1 >= MAX_PATH_SEGMENTS {
            continue;
        }
        for (name, targets) in &exports[m] {
            let path = format!("{}::{}", prefix, name);
            for t in targets {
                match t {
                    Target::Module(j) => {
                        module_paths[*j].push(path.clone());
                        if expansions[*j] < MAX_EXPANSIONS_PER_MODULE {
                            expansions[*j] += 1;
                            queue.push_back((*j, path.clone()));
                        }
                    }
                    Target::Item(kind, i) => item_paths.entry((*kind, *i)).or_default().push(path.clone()),
                    _ => {}
                }
            }
        }
    }
    for paths in module_paths.iter_mut() {
        sort_paths(paths);
    }
    for paths in item_paths.values_mut() {
        sort_paths(paths);
    }
    let paths_of = |kind: ItemKind, index: usize| item_paths.get(&(kind, index)).cloned().unwrap_or_default();
    for (i, f) in c.functions.iter_mut().enumerate() {
        if f.owner.is_none() {
            f.header.public_paths = paths_of(ItemKind::Function, i);
        }
    }
    for (i, s) in c.structs.iter_mut().enumerate() {
        s.header.public_paths = paths_of(ItemKind::Struct, i);
    }
    for (i, e) in c.enums.iter_mut().enumerate() {
        e.header.public_paths = paths_of(ItemKind::Enum, i);
    }
    for (i, a) in c.aliases.iter_mut().enumerate() {
        a.header.public_paths = paths_of(ItemKind::Alias, i);
    }
    for (i, k) in c.constants.iter_mut().enumerate() {
        if k.owner.is_none() {
            k.header.public_paths = paths_of(ItemKind::Constant, i);
        }
    }
    for (i, t) in c.traits.iter_mut().enumerate() {
        t.header.public_paths = paths_of(ItemKind::Trait, i);
        let trait_paths = t.header.public_paths.clone();
        for m in &mut t.methods {
            m.header.public_paths = trait_paths.iter().map(|p| format!("{}::{}", p, m.header.name)).collect();
        }
    }

    // Owners of methods and associated consts.
    for (p, target) in c.pending_owners.iter().zip(owner_targets.iter()) {
        let owner_path = target.as_ref().and_then(|t| target_text(c, t));
        let owner_public: Vec<String> = match target {
            Some(Target::Item(kind, i)) => paths_of(*kind, *i),
            _ => Vec::new(),
        };
        let module_path = c.modules[p.module].path_text();
        match p.kind {
            ItemKind::Function => {
                let f = &mut c.functions[p.index];
                let owner = f.owner.as_mut().expect("method without owner");
                owner.resolved_path = owner_path.clone();
                let base = owner_path.unwrap_or_else(|| format!("{}::{}", module_path, last_segment(&owner.type_text)));
                f.header.canonical_path = format!("{}::{}", base, f.header.name);
                f.header.public_paths = owner_public.iter().map(|o| format!("{}::{}", o, f.header.name)).collect();
            }
            ItemKind::Constant => {
                let k = &mut c.constants[p.index];
                let owner = k.owner.as_mut().expect("associated const without owner");
                owner.resolved_path = owner_path.clone();
                let base = owner_path.unwrap_or_else(|| format!("{}::{}", module_path, last_segment(&owner.type_text)));
                k.header.canonical_path = format!("{}::{}", base, k.header.name);
                k.header.public_paths = owner_public.iter().map(|o| format!("{}::{}", o, k.header.name)).collect();
            }
            _ => {}
        }
    }

    // Trait impls and implemented traits.
    let self_paths: Vec<Option<String>> = impl_targets
        .iter()
        .map(|t| match t {
            Some(Target::Item(kind, i)) => Some(item_path(c, *kind, *i)),
            _ => None,
        })
        .collect();
    let mut implemented: HashMap<(ItemKind, usize), BTreeSet<String>> = HashMap::new();
    for ((p, target), self_path) in c.pending_impls.iter().zip(impl_targets.iter()).zip(self_paths) {
        let imp = &mut c.trait_impls[p.index];
        if let Some(Target::Item(kind, i)) = target {
            let name = if imp.is_negative { format!("!{}", imp.trait_name) } else { imp.trait_name.clone() };
            implemented.entry((*kind, *i)).or_default().insert(name);
        }
        imp.self_path = self_path;
    }
    for (i, s) in c.structs.iter_mut().enumerate() {
        let mut set: BTreeSet<String> = s.derives.iter().map(|d| last_segment(d)).collect();
        if let Some(extra) = implemented.get(&(ItemKind::Struct, i)) {
            set.extend(extra.iter().cloned());
        }
        s.implemented_traits = set.into_iter().collect();
    }
    for (i, e) in c.enums.iter_mut().enumerate() {
        let mut set: BTreeSet<String> = e.derives.iter().map(|d| last_segment(d)).collect();
        if let Some(extra) = implemented.get(&(ItemKind::Enum, i)) {
            set.extend(extra.iter().cloned());
        }
        e.implemented_traits = set.into_iter().collect();
    }

    // Re-exports (pub use).
    let mut reexports = Vec::new();
    let mut unresolved = Vec::new();
    for (m, module) in c.modules.iter().enumerate() {
        for (ui, u) in module.uses.iter().enumerate() {
            if !u.is_pub {
                continue;
            }
            let targets = use_results.get(&(m, ui)).cloned().unwrap_or_default();
            let is_external = !targets.is_empty() && targets.iter().all(|t| matches!(t, Target::External(_)));
            if targets.is_empty() {
                unresolved.push(Diagnostic {
                    severity: Severity::Warning,
                    code: "unresolved_reexport".to_string(),
                    message: format!("{}: `pub use {}` could not be resolved", module.path_text(), u.path.join("::")),
                    file: Some(u.file.clone()),
                    line: Some(u.line),
                    column: None,
                });
            }
            reexports.push(ReexportInfo {
                module: module.path_text(),
                visibility: u.visibility.clone(),
                path: u.path.join("::"),
                name: match &u.leaf {
                    UseLeaf::Name(n) => n.clone(),
                    UseLeaf::Glob => "*".to_string(),
                },
                is_glob: matches!(u.leaf, UseLeaf::Glob),
                targets: targets.iter().filter_map(|t| target_text(c, t)).collect(),
                is_external,
                cfg: u.cfg.clone(),
                file: u.file.clone(),
                line: u.line,
            });
        }
    }
    c.diagnostics.extend(unresolved);
    (reexports, module_paths)
}

fn resolve_type_owner(r: &Resolver, module: usize, ty: &Type) -> Option<Target> {
    let segs = type_path_segments(ty)?;
    if segs.len() == 1 && segs[0] == "Self" {
        return None;
    }
    let found = r.resolve_path(module, &segs, 0);
    found.into_iter().find(|t| matches!(t, Target::Item(ItemKind::Struct | ItemKind::Enum | ItemKind::Alias, _)))
}

fn last_segment(path: &str) -> String {
    let base = path.split('<').next().unwrap_or(path);
    base.rsplit("::").next().unwrap_or(base).trim().to_string()
}
