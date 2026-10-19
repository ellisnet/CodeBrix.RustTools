//! The JSON document (schema version 1). SCHEMA.md describes every field; the C# model in
//! CodeBrix.RustTools.CrateBuilder.Facade.Model mirrors these types exactly. Optional values
//! that are absent are left out of the JSON; lists are always written (possibly empty).

use serde::Serialize;

pub const SCHEMA_VERSION: u32 = 1;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiDocument {
    pub schema_version: u32,
    pub tool: ToolInfo,
    #[serde(rename = "crate")]
    pub krate: CrateInfo,
    pub modules: Vec<ModuleInfo>,
    pub functions: Vec<FunctionInfo>,
    pub structs: Vec<StructInfo>,
    pub enums: Vec<EnumInfo>,
    pub type_aliases: Vec<TypeAliasInfo>,
    pub constants: Vec<ConstantInfo>,
    pub traits: Vec<TraitInfo>,
    pub trait_impls: Vec<TraitImplInfo>,
    pub reexports: Vec<ReexportInfo>,
    pub diagnostics: Vec<Diagnostic>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ToolInfo {
    pub name: String,
    pub version: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CrateInfo {
    pub name: String,
    pub version: String,
    pub edition: String,
    pub lib_name: String,
    pub crate_types: Vec<String>,
    pub root_kind: String,
    pub root_file: String,
    pub manifest_path: String,
    pub package_directory: String,
    pub declared_features: Vec<String>,
    pub requested_features: Vec<String>,
    pub docs: String,
}

/// The fields every item carries (flattened into the item's JSON object).
#[derive(Serialize, Clone, Default)]
#[serde(rename_all = "camelCase")]
pub struct ItemHeader {
    pub id: String,
    pub name: String,
    pub canonical_path: String,
    pub public_paths: Vec<String>,
    pub module: String,
    pub visibility: String,
    pub cfg: Vec<String>,
    pub docs: String,
    pub is_doc_hidden: bool,
    pub attributes: Vec<String>,
    pub file: String,
    pub line: u32,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ModuleInfo {
    pub path: String,
    pub name: String,
    pub visibility: String,
    pub file: String,
    pub is_inline: bool,
    pub cfg: Vec<String>,
    pub docs: String,
    pub public_paths: Vec<String>,
}

#[derive(Serialize, Clone, Default)]
#[serde(rename_all = "camelCase")]
pub struct GenericsInfo {
    pub text: String,
    pub where_clause: String,
    pub lifetimes: Vec<String>,
    pub type_params: Vec<TypeParamInfo>,
    pub const_params: Vec<ConstParamInfo>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct TypeParamInfo {
    pub name: String,
    pub bounds: Vec<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub default: Option<String>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ConstParamInfo {
    pub name: String,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub default: Option<String>,
}

#[derive(Serialize, Clone, Copy, PartialEq, Eq, Debug)]
#[serde(rename_all = "snake_case")]
pub enum TypeKind {
    Primitive,
    Reference,
    Slice,
    Array,
    Path,
    Tuple,
    Unit,
    Ptr,
    ImplTrait,
    DynTrait,
    FnPtr,
    Never,
    Infer,
    Macro,
    Other,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct TypeInfo {
    pub kind: TypeKind,
    pub text: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mutable: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub lifetime: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub inner: Option<Box<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub elem: Option<Box<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub len: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub len_value: Option<u64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub qself: Option<Box<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub segments: Option<Vec<PathSegmentInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub elems: Option<Vec<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub bounds: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub inputs: Option<Vec<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub output: Option<Box<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub is_unsafe: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub abi: Option<String>,
    /// For a path type that names a struct, enum or type alias of this crate: that item's
    /// canonical path, resolved in the module where the type was written (its `use`
    /// declarations, glob imports, `self` / `super` / `crate`).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub resolved: Option<String>,
    /// For a path type whose first segment is brought in by a `use` of ANOTHER crate (or std)
    /// in the module where the type was written: `Some(true)`, so a crate item of the same
    /// name is never taken for it.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub external: Option<bool>,
    /// For a path type whose first segment names nothing declared or imported in the module
    /// where the type was written (a prelude or std type such as `Result`, or a generic
    /// parameter): `Some(true)`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub unresolved: Option<bool>,
}

impl TypeInfo {
    pub fn new(kind: TypeKind, text: String) -> TypeInfo {
        TypeInfo {
            kind,
            text,
            name: None,
            mutable: None,
            lifetime: None,
            inner: None,
            elem: None,
            len: None,
            len_value: None,
            qself: None,
            segments: None,
            elems: None,
            bounds: None,
            inputs: None,
            output: None,
            is_unsafe: None,
            abi: None,
            resolved: None,
            external: None,
            unresolved: None,
        }
    }

    pub fn unit() -> TypeInfo {
        TypeInfo::new(TypeKind::Unit, "()".to_string())
    }
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct PathSegmentInfo {
    pub name: String,
    pub args: Vec<TypeInfo>,
    pub lifetimes: Vec<String>,
    pub const_args: Vec<String>,
    pub bindings: Vec<AssocBindingInfo>,
    pub other_args: Vec<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fn_inputs: Option<Vec<TypeInfo>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fn_output: Option<Box<TypeInfo>>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct AssocBindingInfo {
    pub name: String,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct OwnerInfo {
    pub type_text: String,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub resolved_path: Option<String>,
    pub generics: GenericsInfo,
}

#[derive(Serialize, Clone, Copy, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum SelfKind {
    None,
    Value,
    MutValue,
    Ref,
    RefMut,
    Typed,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ParamInfo {
    pub name: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub pattern: Option<String>,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct FunctionInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub owner: Option<OwnerInfo>,
    pub self_kind: SelfKind,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub self_type: Option<TypeInfo>,
    pub params: Vec<ParamInfo>,
    pub return_type: TypeInfo,
    pub generics: GenericsInfo,
    pub has_generics: bool,
    pub is_unsafe: bool,
    pub is_async: bool,
    pub is_const: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub abi: Option<String>,
    pub signature: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub has_default: Option<bool>,
}

#[derive(Serialize, Clone, Copy, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum Shape {
    Named,
    Tuple,
    Unit,
    Union,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct FieldInfo {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,
    pub index: u32,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
    pub visibility: String,
    pub cfg: Vec<String>,
    pub docs: String,
    pub attributes: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StructInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    pub shape: Shape,
    pub fields: Vec<FieldInfo>,
    pub tuple_arity: u32,
    pub derives: Vec<String>,
    pub repr: Vec<String>,
    pub generics: GenericsInfo,
    pub has_generics: bool,
    pub implemented_traits: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VariantInfo {
    pub name: String,
    pub shape: Shape,
    pub fields: Vec<FieldInfo>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub discriminant: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub discriminant_value: Option<i64>,
    pub cfg: Vec<String>,
    pub docs: String,
    pub attributes: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EnumInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    pub variants: Vec<VariantInfo>,
    pub is_c_like: bool,
    pub derives: Vec<String>,
    pub repr: Vec<String>,
    pub generics: GenericsInfo,
    pub has_generics: bool,
    pub implemented_traits: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TypeAliasInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    pub generics: GenericsInfo,
    pub has_generics: bool,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
}

#[derive(Serialize, Clone, Copy, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum ConstantKind {
    Const,
    Static,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ConstantInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    pub kind: ConstantKind,
    pub is_mutable: bool,
    #[serde(rename = "type")]
    pub ty: TypeInfo,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub value: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub owner: Option<OwnerInfo>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TraitInfo {
    #[serde(flatten)]
    pub header: ItemHeader,
    pub generics: GenericsInfo,
    pub has_generics: bool,
    pub supertraits: Vec<String>,
    pub is_unsafe: bool,
    pub is_auto: bool,
    pub methods: Vec<FunctionInfo>,
    pub associated_types: Vec<String>,
    pub associated_consts: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TraitImplInfo {
    pub module: String,
    pub file: String,
    pub line: u32,
    pub cfg: Vec<String>,
    pub trait_path: String,
    pub trait_name: String,
    pub self_type: TypeInfo,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub self_path: Option<String>,
    pub generics: GenericsInfo,
    pub is_unsafe: bool,
    pub is_negative: bool,
    pub methods: Vec<String>,
    pub associated_types: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ReexportInfo {
    pub module: String,
    pub visibility: String,
    pub path: String,
    pub name: String,
    pub is_glob: bool,
    pub targets: Vec<String>,
    pub is_external: bool,
    pub cfg: Vec<String>,
    pub file: String,
    pub line: u32,
}

#[derive(Serialize, Clone, Copy, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum Severity {
    Info,
    Warning,
    Error,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Diagnostic {
    pub severity: Severity,
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub file: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub line: Option<u32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub column: Option<u32>,
}
