using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A <c>Copy</c> upstream struct whose fields are all public and C-representable, passed by
/// value through a <c>#[repr(C)]</c> mirror (a blittable struct on the managed side).
/// </summary>
internal sealed class MirrorStruct
{
    /// <summary>The type's path inside the crate, shortest public form without
    /// <c>crate::</c>.</summary>
    public string RustPath { get; set; }

    /// <summary>The Rust type name.</summary>
    public string RustTypeName { get; set; }

    /// <summary>The C struct name: <c>&lt;prefix&gt;&lt;snake name&gt;</c>.</summary>
    public string CName { get; set; }

    /// <summary>The managed struct name.</summary>
    public string ManagedName { get; set; }

    /// <summary>The type's Rust doc comments.</summary>
    public string Doc { get; set; }

    /// <summary>The fields in declaration order (the C layout order).</summary>
    public IList<MirrorField> Fields { get; set; } = new List<MirrorField>();

    /// <summary>The upstream type is a tuple struct (fields <c>item0</c>, <c>item1</c> ...;
    /// the upstream fields are <c>.0</c>, <c>.1</c> ...).</summary>
    public bool IsTupleStruct { get; set; }

    /// <summary>The number of fields of a tuple struct; 0 otherwise.</summary>
    public int TupleArity { get; set; }

    /// <summary>ADDED (WP6): the C size in bytes (64-bit targets, natural alignment).</summary>
    public int ByteSize { get; set; }

    /// <summary>ADDED (WP6): the C alignment in bytes.</summary>
    public int Alignment { get; set; }
}
