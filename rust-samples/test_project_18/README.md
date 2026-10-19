# test_project_18 - workspace with path dependencies (staging fixture)

A small **Cargo workspace** used by the CodeBrix.RustTools.CrateBuilder staging tests
(copying a crate's workspace to a scratch folder, following its path dependencies and
rewriting the ones that leave the copied tree). It has **no external crates** and builds
offline.

| Crate            | Kind      | Member? | Purpose |
|------------------|-----------|---------|---------|
| `crates/alpha`   | rlib      | yes     | `transform(n) = (n + 1) * 2`; depends on `beta` (workspace dependency) and on `gamma` by the relative path `../../external/gamma`. Has one unit test. |
| `crates/beta`    | library   | yes     | `increment(n)`; referenced from `[workspace.dependencies]` by path. |
| `external/gamma` | library   | no      | `double(n)`; a standalone crate inside the workspace folder, excluded with `exclude = ["external"]`. Its version (`1.2.0`) is its own. |

## Notable manifest features

* `resolver = "3"`, `members = ["crates/*"]` (a glob), `exclude = ["external"]`
* `[workspace.package]` - version (`0.18.2`), edition (`2024`), license and publish,
  inherited by `alpha` and `beta` with `field.workspace = true`
* `[workspace.dependencies]` - the `beta` path dependency, inherited with
  `beta.workspace = true`
* `[lib] crate-type = ["rlib"]` on `alpha`
* a trailing comment on alpha's `gamma` line (the staging tests check that a path
  rewrite keeps it)
* One shared `Cargo.lock` at the workspace root

## Run (from this folder)

```sh
cargo build --offline        # builds alpha, beta and gamma
cargo test --offline         # runs alpha's unit test
```

No network access. Nothing is written outside `target/`.

## Crates

None.
