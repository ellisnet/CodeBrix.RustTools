# test_project_17 - plain Rust library (rlib) with shapes a C facade cannot map automatically

A pure-Rust package named `hardcrate` with **no dependencies** and **no C exports**. Its public
API is made of the shapes that CodeBrix.RustTools.CrateBuilder's mapping planner does NOT map
automatically, so the tests can check that they are skipped and reported (with the override a
developer would need):

| Function / type | Shape |
|-----------------|-------|
| `max_of<T: PartialOrd>(a: T, b: T) -> T` | generic function |
| `describe(value: impl Display) -> String` | `impl Trait` parameter |
| `each(xs: &[i32], f: impl FnMut(i32))` | closure (callback) parameter |
| `apply(f: &dyn Fn(i32) -> i32, x: i32) -> i32` | trait object |
| `evens(xs: &[i32]) -> impl Iterator<Item = i32>` | iterator return |
| `Shape { Circle(f64), Rect { w, h } }` | data-carrying enum |
| `total_area(Vec<Shape>)`, `unit_squares(usize) -> Vec<Shape>` | `Vec` of a non-`Copy` enum in and out |
| `Names` (`Rc<Vec<String>>`, `Arc<String>` fields) with `first(&self) -> &str` | shared ownership, borrowed return |
| `doubled_later(u32) -> u32` | `async fn` |

Two functions ARE mappable, so the wrap report is never empty: `add(i32, i32) -> i32` and
`scale(f64, f64) -> f64`. The manifest declares two features (`default`, `extra`).

## Run

```sh
cargo test
```

Run it in a copy of this folder (or delete `target/` afterwards); see the repository's
EXTRAS-README.txt.
