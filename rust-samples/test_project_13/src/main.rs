//! Calls the library's C-ABI function through the rlib and prints the result.

fn main() {
    let sum = test_project_13::cb_add(2, 3);
    println!("test_project_13: cb_add(2, 3) = {sum}");
}
