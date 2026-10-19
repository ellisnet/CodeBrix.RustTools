# codebrix-api-extractor JSON document - schema version 1

`codebrix-api-extractor` reads the public API of one Rust crate with `syn` and writes it as ONE
JSON document (UTF-8, indented, `\n` line endings). It REPORTS; it never decides:

* no macro expansion (items an item macro would generate are absent; an `info` diagnostic says so),
* no type inference, no trait resolution beyond the syntax,
* no `cfg` evaluation (every `cfg` predicate is recorded verbatim; the consumer decides),
* name resolution is syntactic: a path that does not resolve inside the crate is EXTERNAL
  (another crate or std).

The C# mirror is `CodeBrix.RustTools.CrateBuilder.Facade.Model.CrateApiModel`.

## Command line

```
codebrix-api-extractor --manifest <Cargo.toml or its folder> [--crate <name>] --out <file.json | -> [--features a,b]
```

| Option | Meaning |
|--------|---------|
| `--manifest` | The package's Cargo.toml (or its folder). A virtual workspace manifest needs `--crate`. |
| `--crate` | The package to read: matched against the package name and the library name, `-` and `_` equal. With a `[package]` manifest it must match that package; with a workspace manifest it selects the member (members as listed, `dir/*` expanded one level). |
| `--out` | The JSON file to write (folders are created), or `-` for standard output. |
| `--features` | Comma- or space-separated features; recorded in `crate.requestedFeatures` only (nothing is evaluated). |

Exit codes: `0` written (diagnostics may be present) - `1` usage error - `2` no crate root or an
unusable manifest (message on standard error) - `3` the output could not be written.

The crate root is `[lib] path`, else `src/lib.rs`. A package without a library falls back to the
binary root (`[[bin]]` 0 `path`, else `src/main.rs`) with `crate.rootKind = "bin"` and a
`no_library_target` warning; neither present = exit 2.

## Conventions

* Property names are camelCase. Enumerated values are snake_case strings.
* An optional value that is absent is LEFT OUT of the JSON (never `null`); every list is always
  written (possibly empty); booleans are always written.
* Paths are written with `::` and start with `crate` (`crate::shapes::Rect`). File paths are
  relative to the package folder, with `/`.
* Order: source order, depth first through the module tree: the items of a module appear at the
  place of its `mod` declaration, before the items that follow that declaration. The output is
  deterministic for the same input.
* `text` of a type, `signature`, `generics.text`, `whereClause`, attribute and `cfg` strings
  are the tokens printed with rustfmt-like spacing: `&[u8]`, `Vec<u8>`, `[[f64; 3]; 3]`,
  `impl FnMut(i32)`, `T: PartialOrd + Copy`, `target_arch = "wasm32"`.

## What is recorded

* Every item whose visibility is `pub`, wherever it is declared (also inside private modules).
  `pub(crate)`, `pub(super)`, `pub(in ...)` and private items are DROPPED. Whether code outside
  the crate can reach an item is `publicPaths` (empty = not reachable).
* Inherent-impl methods and associated consts: only `pub` ones, in `functions` / `constants`
  with an `owner`.
* Trait impls (`impl Trait for Type`): all of them, informational, in `traitImpls`; their trait
  names are also merged into `implementedTraits` of the local struct / enum they target.
* `pub use` declarations: in `reexports` (private `use` declarations are only used for
  resolution).
* Function bodies are not read (items declared inside a function body are not recorded).

## Top level

```json
{
  "schemaVersion": 1,
  "tool": { "name": "codebrix-api-extractor", "version": "1.0.0" },
  "crate": { },
  "modules": [ ],
  "functions": [ ],
  "structs": [ ],
  "enums": [ ],
  "typeAliases": [ ],
  "constants": [ ],
  "traits": [ ],
  "traitImpls": [ ],
  "reexports": [ ],
  "diagnostics": [ ]
}
```

## crate

| Property | Type | Meaning |
|----------|------|---------|
| `name` | string | `[package] name` as written (`my-crate`). |
| `version` | string | `[package] version`; `"workspace"` when inherited (`version.workspace = true`); `"0.0.0"` when absent. |
| `edition` | string | `[package] edition`; `"workspace"` when inherited; `"2015"` when absent (Cargo's default). |
| `libName` | string | `[lib] name`, else the package name with `-` -> `_`. The base of the native file name. |
| `crateTypes` | string[] | `[lib] crate-type` as written; `["lib"]` when absent. |
| `rootKind` | `"lib"` \| `"bin"` | Which root was read (see above). |
| `rootFile` | string | The root file, relative (`src/lib.rs`). |
| `manifestPath` | string | The manifest that was read, as the tool saw it. |
| `packageDirectory` | string | The package folder, as the tool saw it. |
| `declaredFeatures` | string[] | The keys of `[features]`, in file order (`default` included when declared). |
| `requestedFeatures` | string[] | The `--features` values. |
| `docs` | string | The crate-level `//!` docs (`""` when none). |

## Item header (every function, struct, enum, type alias, constant and trait)

| Property | Type | Meaning |
|----------|------|---------|
| `id` | string | Unique in the document: `<kind>:<canonicalPath>`, kind = `fn`, `struct`, `union`, `enum`, `type`, `const`, `static`, `trait`. A repeat (for example two cfg-alternative definitions) gets `#2`, `#3`, ... Trait methods: `fn:<trait path>::<name>`. |
| `name` | string | The identifier. |
| `canonicalPath` | string | Where it is declared: module path + name (`crate::time::Tick`). Methods and associated consts: owner path + name (`crate::Vec2::dot`); when the owner type cannot be resolved, module path + the owner's last segment + name. |
| `publicPaths` | string[] | Every path by which code OUTSIDE the crate can name it: through `pub mod`s and `pub use` re-exports, named and glob, renames included. Shortest first, then alphabetical. Empty = not reachable from outside. Methods: each public path of the owner + `::name`. |
| `module` | string | The declaring module's path. |
| `visibility` | string | `"pub"` for every recorded item (trait methods: `"pub"`). |
| `cfg` | string[] | Every `#[cfg(...)]` predicate that applies, outermost first: enclosing modules' (including `#![cfg]` in a module file), then the impl block's, then the item's own. All must hold. |
| `docs` | string | The doc comments joined with `\n`, common indentation removed, leading/trailing blank lines dropped (`""` when none). |
| `isDocHidden` | bool | `#[doc(hidden)]` is present. |
| `attributes` | string[] | Every other attribute, verbatim (`#[inline]`, `#[must_use]`, `#[unsafe(no_mangle)]`, `#[deprecated(...)]`, `#[cfg_attr(...)]`, `#[doc(hidden)]`). Not included: doc strings, `cfg`, `derive`, `repr` (they have their own fields). |
| `file` | string | The source file, relative. |
| `line` | number | The line of the item's name (1-based). |

## Types (`type`, `returnType`, field types, ...)

Every type node has `kind` and `text`; the other properties appear only for the kinds listed.
Parentheses and invisible groups are unwrapped.

| `kind` | Extra properties | Example `text` |
|--------|------------------|----------------|
| `primitive` | `name` (`i8`..`i128`, `isize`, `u8`..`u128`, `usize`, `f16`, `f32`, `f64`, `f128`, `bool`, `char`, `str`) | `u32` |
| `reference` | `mutable` (bool), `lifetime` (when written, `'a`), `inner` (type) | `&mut [u8]` |
| `slice` | `elem` | `[i32]` |
| `array` | `elem`, `len` (the length expression, verbatim), `lenValue` (number, when `len` is an integer literal) | `[[f64; 3]; 3]` |
| `path` | `segments` (see below), `qself` (the `T` of `<T as Trait>::X`, when present), `resolved` (the canonical path of the crate's own struct, enum or type alias the path names, resolved in the module where it was written - its `use` declarations, glob imports, `self` / `super` / `crate`; absent when it names anything else or more than one item), `external` (`true` when the first segment is brought in by a `use` of another crate or std in that module, so a crate item of the same name is not meant), `unresolved` (`true` when the first segment names nothing declared or imported in that module: a prelude or std type, or a generic parameter) | `Option<usize>`, `Self`, `std::rc::Rc<T>` |
| `tuple` | `elems` (types; one or more - a one-element tuple is written `(T,)`) | `(i32, i32)` |
| `unit` | - | `()` (also the return type of a function without `->`) |
| `ptr` | `mutable` (bool: `*mut`), `inner` | `*const u8` |
| `impl_trait` | `bounds` (string[], verbatim) | `impl FnMut(i32)` |
| `dyn_trait` | `bounds` (string[], verbatim) | `dyn Fn(i32) -> i32` |
| `fn_ptr` | `inputs` (types), `output` (type; `unit` when none), `isUnsafe` (bool), `abi` (when `extern`; `"C"` for a bare `extern`) | `extern "C" fn(i32) -> i32` |
| `never` | - | `!` |
| `infer` | - | `_` |
| `macro` | - | `my_type!()` |
| `other` | - | anything else syn models |

Path segment (`segments[]`):

| Property | Type | Meaning |
|----------|------|---------|
| `name` | string | The identifier (`Vec`, `Option`, `Self`, `std`). |
| `args` | type[] | Type arguments in `<...>` (`Vec<u8>` -> one `primitive` u8). |
| `lifetimes` | string[] | Lifetime arguments (`'a`, `'_`). |
| `constArgs` | string[] | Const arguments written as expressions/blocks (`{ N + 1 }`); a bare `N` parses as a type and is in `args`. |
| `bindings` | `{ name, type }[]` | Associated type bindings (`Iterator<Item = i32>`). |
| `otherArgs` | string[] | Anything else (associated consts, constraints), verbatim. |
| `fnInputs` | type[] | Only for parenthesized arguments: `Fn(A, B) -> C`. |
| `fnOutput` | type | Only with `fnInputs`; `unit` when there is no `->`. |

`Self` is not substituted: a method returning `Self` has a `path` return type with one segment
named `Self`; the owner is in `owner`.

## Generics (`generics`)

| Property | Type | Meaning |
|----------|------|---------|
| `text` | string | The parameter list verbatim (`<'a, T: Clone = u8, const N: usize = 3>`), `""` when none. |
| `whereClause` | string | The where clause verbatim (`where T: Send`), `""` when none. |
| `lifetimes` | string[] | Lifetime parameters (`'a`). |
| `typeParams` | `{ name, bounds: string[], default? }[]` | Type parameters; `default` is verbatim. |
| `constParams` | `{ name, type, default? }[]` | Const parameters. |

`hasGenerics` (on functions, structs, enums, aliases, traits) = there is at least one TYPE or
CONST parameter (lifetimes alone do not count). For a method it also counts the impl block's
type/const parameters.

## modules[]

The crate root first (`path` = `crate`), then every module in walk order, `pub` or not.

| Property | Type | Meaning |
|----------|------|---------|
| `path` | string | `crate::shapes`. |
| `name` | string | The last segment (`crate` for the root). |
| `visibility` | string | `pub`, `pub(crate)`, `pub(super)`, `pub(self)`, `pub(in path)` or `private`. |
| `file` | string | The file holding its items (the declaring file for an inline `mod x { }`, and for a module whose file was not found). |
| `isInline` | bool | Declared as `mod x { ... }`. |
| `cfg` | string[] | As in the item header. |
| `docs` | string | Outer docs on the `mod` declaration, then the module file's `//!` docs. |
| `publicPaths` | string[] | Paths by which outside code can name the module (`crate` for the root). |

Module files: `mod x;` -> `x.rs` or `x/mod.rs` next to a mod-rs file (`lib.rs`, `main.rs`,
`mod.rs`, a `#[path]` file), else in the folder named after the current file; `#[path = "..."]`
is relative to the current file's folder (inside an inline module: to that module's folder).

## functions[]

Free functions and inherent-impl methods (only `pub` ones). Item header plus:

| Property | Type | Meaning |
|----------|------|---------|
| `owner` | object | Only for methods: `typeText` (the impl's self type, verbatim), `type` (structured), `resolvedPath` (canonical path of the local struct/enum/union/alias it names, when resolved), `generics` (the impl block's). |
| `selfKind` | `none` \| `value` \| `mut_value` \| `ref` \| `ref_mut` \| `typed` | `none` = no receiver (free fn or associated fn such as `new`); `value` = `self`; `mut_value` = `mut self`; `ref` = `&self`; `ref_mut` = `&mut self`; `typed` = `self: Box<Self>` etc. |
| `selfType` | type | Only for `typed`: the receiver type. |
| `params` | `{ name, pattern?, type }[]` | The parameters after the receiver. `name` is the identifier; for a destructuring pattern it is `arg<N>` (N = position counting the receiver) and `pattern` holds the pattern verbatim. |
| `returnType` | type | `unit` when there is no `->`. |
| `generics` | generics | The function's own. |
| `hasGenerics` | bool | See Generics. |
| `isUnsafe`, `isAsync`, `isConst` | bool | `unsafe fn`, `async fn`, `const fn`. |
| `abi` | string | Only for `extern "..." fn` (`"C"`; a bare `extern` gives `"C"`). |
| `signature` | string | The whole signature verbatim (`pub fn encode(&mut self, input: &[u8]) -> Vec<u8>`). |
| `hasDefault` | bool | Only for trait methods (inside `traits[].methods`): the trait gives a default body. |

## structs[]

Structs and unions. Item header plus:

| Property | Type | Meaning |
|----------|------|---------|
| `shape` | `named` \| `tuple` \| `unit` \| `union` | `struct S { a: T }`, `struct S(T);`, `struct S;`, `union U { }`. |
| `fields` | field[] | All fields, public or not (see below). |
| `tupleArity` | number | Number of fields of a tuple struct; 0 otherwise. |
| `derives` | string[] | `#[derive(...)]` paths as written (`Clone`, `serde::Serialize`). |
| `repr` | string[] | `#[repr(...)]` hints verbatim (`C`, `u8`, `transparent`, `align(8)`). |
| `generics`, `hasGenerics` | | See Generics. |
| `implementedTraits` | string[] | Sorted, distinct: the last segment of every derive plus the trait name of every `impl Trait for ThisType` found (`!Send` for a negative impl). `Copy`, `Clone`, `Default`, `Drop`, `Display`, `Debug`, `PartialEq` ... |

Field: `name` (absent for tuple fields), `index` (0-based), `type`, `visibility` (as for
modules), `cfg` (the field's own), `docs`, `attributes`.

## enums[]

Item header plus `variants`, `isCLike` (every variant is unit-shaped), `derives`, `repr`,
`generics`, `hasGenerics`, `implementedTraits` (as for structs).

Variant: `name`, `shape` (`unit` \| `tuple` \| `named`), `fields` (as for structs),
`discriminant` (the `= expr` verbatim, when written), `discriminantValue` (number, when the
discriminant is an integer literal, optionally negative), `cfg`, `docs`, `attributes`.

## typeAliases[]

Item header plus `generics`, `hasGenerics`, `type` (the aliased type).

## constants[]

`const` and `static` items, and `pub const` items of inherent impls. Item header plus:

| Property | Type | Meaning |
|----------|------|---------|
| `kind` | `const` \| `static` | |
| `isMutable` | bool | `static mut`. |
| `type` | type | |
| `value` | string | Only when the value is a literal or a negated literal, verbatim (`254_016_000_000`, `-5`, `"text"`). |
| `owner` | object | Only for associated consts (as for methods). |

## traits[]

Item header plus `generics`, `hasGenerics`, `supertraits` (verbatim), `isUnsafe`, `isAuto`,
`methods` (function objects: no `owner`; `hasDefault` set; `publicPaths` = the trait's paths +
`::name`), `associatedTypes` (names), `associatedConsts` (names). Informational.

## traitImpls[]

Every `impl Trait for Type` (also on non-public or external types). Informational.

| Property | Type | Meaning |
|----------|------|---------|
| `module`, `file`, `line`, `cfg` | | Where the impl is. |
| `traitPath` | string | As written (`fmt::Display`, `Default`). |
| `traitName` | string | Its last segment (`Display`). |
| `selfType` | type | The implementing type. |
| `selfPath` | string | The canonical path of the local struct/enum/union/alias, when resolved. |
| `generics` | generics | The impl block's. |
| `isUnsafe`, `isNegative` | bool | `unsafe impl`, `impl !Trait for`. |
| `methods` | string[] | Names of the methods the impl defines. |
| `associatedTypes` | string[] | Names of the associated types it defines. |

## reexports[]

One entry per name of every `pub use` (a `use a::{b, c}` gives two).

| Property | Type | Meaning |
|----------|------|---------|
| `module` | string | The module holding the `use`. |
| `visibility` | string | Always `pub` (restricted `pub(...)` uses are not listed). |
| `path` | string | The path as written, without the renamed name (`shapes::Rect`, `self::nested::f`, `hidden` for `hidden::*`). |
| `name` | string | The exported name (the rename when `as` is used), `*` for a glob. |
| `isGlob` | bool | `use path::*`. |
| `targets` | string[] | What it resolved to: canonical paths of local items/modules, or the external path. |
| `isExternal` | bool | Every target is outside the crate. |
| `cfg`, `file`, `line` | | |

## diagnostics[]

| Property | Type | Meaning |
|----------|------|---------|
| `severity` | `info` \| `warning` \| `error` | |
| `code` | string | `parse_error` (a file did not parse; its items are missing, the walk goes on), `file_unreadable`, `module_file_not_found`, `item_macro_not_expanded` (info), `unsupported_item`, `unresolved_reexport`, `no_library_target`. |
| `message` | string | Human-readable. |
| `file`, `line`, `column` | | When known (1-based). |
