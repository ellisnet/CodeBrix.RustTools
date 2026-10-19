//! codebrix-api-extractor: reads the public API of a Rust crate (syn, no macro expansion, no
//! type inference, no cfg evaluation) and writes it as ONE JSON document (SCHEMA.md).
//!
//! Usage: codebrix-api-extractor --manifest <Cargo.toml> [--crate <name>] --out <json | ->
//!        [--features a,b]
//! Exit codes: 0 = written (diagnostics may be present), 1 = usage error, 2 = no crate root /
//! unusable manifest, 3 = the output could not be written.

mod manifest;
mod model;
mod resolve;
mod types;
mod walk;

use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::ExitCode;

use model::{ApiDocument, CrateInfo, Diagnostic, ModuleInfo, Severity, ToolInfo, SCHEMA_VERSION};
use walk::{relative, Collected};

struct Args {
    manifest: PathBuf,
    crate_name: Option<String>,
    out: String,
    features: Vec<String>,
}

fn usage() -> &'static str {
    "usage: codebrix-api-extractor --manifest <Cargo.toml> [--crate <name>] --out <file.json | -> [--features a,b]"
}

fn parse_args() -> Result<Args, String> {
    let mut manifest = None;
    let mut crate_name = None;
    let mut out = None;
    let mut features = Vec::new();
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        let mut value = |name: &str| args.next().ok_or_else(|| format!("{} needs a value", name));
        match arg.as_str() {
            "--manifest" => manifest = Some(PathBuf::from(value("--manifest")?)),
            "--crate" => crate_name = Some(value("--crate")?),
            "--out" => out = Some(value("--out")?),
            "--features" => {
                features.extend(value("--features")?.split([',', ' ']).filter(|f| !f.is_empty()).map(str::to_string))
            }
            "--help" | "-h" => return Err(usage().to_string()),
            other => return Err(format!("unknown argument '{}'\n{}", other, usage())),
        }
    }
    Ok(Args {
        manifest: manifest.ok_or_else(|| format!("--manifest is required\n{}", usage()))?,
        crate_name,
        out: out.ok_or_else(|| format!("--out is required\n{}", usage()))?,
        features,
    })
}

/// The package that is read, after workspace member selection.
struct Package {
    manifest_path: PathBuf,
    dir: PathBuf,
    doc: manifest::Document,
}

fn normalize(name: &str) -> String {
    name.replace('-', "_")
}

fn load_manifest(path: &Path) -> Result<manifest::Document, String> {
    let text = fs::read_to_string(path).map_err(|e| format!("{}: {}", path.display(), e))?;
    manifest::parse(&text).map_err(|e| format!("{}: {}", path.display(), e))
}

fn package_matches(doc: &manifest::Document, wanted: &str) -> bool {
    let name = doc.get_str(&["package", "name"]).unwrap_or_default();
    let lib = doc.get_str(&["lib", "name"]).unwrap_or_else(|| normalize(&name));
    normalize(&name) == normalize(wanted) || normalize(&lib) == normalize(wanted)
}

fn select_package(args: &Args) -> Result<Package, String> {
    let manifest_path = if args.manifest.is_dir() { args.manifest.join("Cargo.toml") } else { args.manifest.clone() };
    if !manifest_path.is_file() {
        return Err(format!("the manifest {} does not exist", manifest_path.display()));
    }
    let dir = manifest_path.parent().map(Path::to_path_buf).unwrap_or_else(|| PathBuf::from("."));
    let doc = load_manifest(&manifest_path)?;
    let has_package = doc.get_str(&["package", "name"]).is_some();
    if has_package {
        if let Some(wanted) = &args.crate_name
            && !package_matches(&doc, wanted) {
                return Err(format!(
                    "--crate {} does not match the package of {} ({})",
                    wanted,
                    manifest_path.display(),
                    doc.get_str(&["package", "name"]).unwrap_or_default()
                ));
            }
        return Ok(Package { manifest_path, dir, doc });
    }
    // A virtual workspace manifest: find the member.
    let members = doc.get(&["workspace", "members"]).and_then(|v| v.as_str_array()).unwrap_or_default();
    let mut candidates: Vec<PathBuf> = Vec::new();
    for member in &members {
        if let Some(prefix) = member.strip_suffix("/*") {
            if let Ok(entries) = fs::read_dir(dir.join(prefix)) {
                let mut found: Vec<PathBuf> = entries.filter_map(|e| e.ok()).map(|e| e.path()).filter(|p| p.join("Cargo.toml").is_file()).collect();
                found.sort();
                candidates.extend(found);
            }
        } else {
            candidates.push(dir.join(member));
        }
    }
    let Some(wanted) = &args.crate_name else {
        return Err(format!(
            "{} is a workspace manifest without a [package]; pass --crate <name> (members: {})",
            manifest_path.display(),
            members.join(", ")
        ));
    };
    for candidate in candidates {
        let member_manifest = candidate.join("Cargo.toml");
        if let Ok(member_doc) = load_manifest(&member_manifest)
            && package_matches(&member_doc, wanted) {
                return Ok(Package { manifest_path: member_manifest, dir: candidate, doc: member_doc });
            }
    }
    Err(format!("no member of the workspace {} is named {}", manifest_path.display(), wanted))
}

fn inherited_or(doc: &manifest::Document, key: &str, default: &str) -> String {
    if let Some(v) = doc.get_str(&["package", key]) {
        return v;
    }
    if doc.get(&["package", key, "workspace"]).and_then(|v| v.as_bool()) == Some(true) {
        return "workspace".to_string();
    }
    default.to_string()
}

fn run(args: &Args) -> Result<ApiDocument, (u8, String)> {
    let package = select_package(args).map_err(|e| (2, e))?;
    let doc = &package.doc;
    let name = doc.get_str(&["package", "name"]).unwrap_or_default();
    let lib_name = doc.get_str(&["lib", "name"]).unwrap_or_else(|| normalize(&name));
    let crate_types = doc
        .get(&["lib", "crate-type"])
        .or_else(|| doc.get(&["lib", "crate_type"]))
        .and_then(|v| v.as_str_array())
        .unwrap_or_else(|| vec!["lib".to_string()]);
    let mut diagnostics: Vec<Diagnostic> = Vec::new();

    let lib_path = doc.get_str(&["lib", "path"]).map(|p| package.dir.join(p)).unwrap_or_else(|| package.dir.join("src").join("lib.rs"));
    let (root, root_kind) = if lib_path.is_file() {
        (lib_path, "lib")
    } else {
        let bin_path = if doc.array_table_count("bin") > 0 {
            doc.get_str(&["bin", "0", "path"]).map(|p| package.dir.join(p))
        } else {
            None
        }
        .unwrap_or_else(|| package.dir.join("src").join("main.rs"));
        if !bin_path.is_file() {
            return Err((
                2,
                format!(
                    "no crate root: neither the library root {} nor a binary root {} exists",
                    lib_path.display(),
                    bin_path.display()
                ),
            ));
        }
        diagnostics.push(Diagnostic {
            severity: Severity::Warning,
            code: "no_library_target".to_string(),
            message: format!(
                "{} has no library target; the binary root {} was read instead (a binary has no public API for other crates)",
                name,
                relative(&package.dir, &bin_path)
            ),
            file: None,
            line: None,
            column: None,
        });
        (bin_path, "bin")
    };

    let mut collected = Collected::walk_crate(&package.dir, &root);
    let (reexports, module_paths) = resolve::resolve(&mut collected);
    walk::assign_ids(&mut collected);
    diagnostics.append(&mut collected.diagnostics);

    let modules: Vec<ModuleInfo> = collected
        .modules
        .iter()
        .enumerate()
        .map(|(i, m)| ModuleInfo {
            path: m.path_text(),
            name: m.path.last().cloned().unwrap_or_default(),
            visibility: m.visibility.clone(),
            file: m.file.clone(),
            is_inline: m.inline,
            cfg: m.cfg.clone(),
            docs: m.docs.clone(),
            public_paths: module_paths.get(i).cloned().unwrap_or_default(),
        })
        .collect();

    Ok(ApiDocument {
        schema_version: SCHEMA_VERSION,
        tool: ToolInfo { name: env!("CARGO_PKG_NAME").to_string(), version: env!("CARGO_PKG_VERSION").to_string() },
        krate: CrateInfo {
            name: name.clone(),
            version: inherited_or(doc, "version", "0.0.0"),
            edition: inherited_or(doc, "edition", "2015"),
            lib_name,
            crate_types,
            root_kind: root_kind.to_string(),
            root_file: relative(&package.dir, &root),
            manifest_path: package.manifest_path.to_string_lossy().to_string(),
            package_directory: package.dir.to_string_lossy().to_string(),
            declared_features: doc.keys_below("features"),
            requested_features: args.features.clone(),
            docs: collected.crate_docs.clone(),
        },
        modules,
        functions: collected.functions,
        structs: collected.structs,
        enums: collected.enums,
        type_aliases: collected.aliases,
        constants: collected.constants,
        traits: collected.traits,
        trait_impls: collected.trait_impls,
        reexports,
        diagnostics,
    })
}

fn main() -> ExitCode {
    let args = match parse_args() {
        Ok(a) => a,
        Err(e) => {
            eprintln!("{}", e);
            return ExitCode::from(1);
        }
    };
    let document = match run(&args) {
        Ok(d) => d,
        Err((code, message)) => {
            eprintln!("codebrix-api-extractor: {}", message);
            return ExitCode::from(code);
        }
    };
    let mut json = match serde_json::to_string_pretty(&document) {
        Ok(j) => j,
        Err(e) => {
            eprintln!("codebrix-api-extractor: {}", e);
            return ExitCode::from(3);
        }
    };
    json.push('\n');
    let written = if args.out == "-" {
        std::io::stdout().write_all(json.as_bytes()).and_then(|_| std::io::stdout().flush())
    } else {
        let out = PathBuf::from(&args.out);
        if let Some(parent) = out.parent().filter(|p| !p.as_os_str().is_empty()) {
            let _ = fs::create_dir_all(parent);
        }
        fs::write(&out, json.as_bytes())
    };
    if let Err(e) = written {
        eprintln!("codebrix-api-extractor: cannot write {}: {}", args.out, e);
        return ExitCode::from(3);
    }
    ExitCode::SUCCESS
}
