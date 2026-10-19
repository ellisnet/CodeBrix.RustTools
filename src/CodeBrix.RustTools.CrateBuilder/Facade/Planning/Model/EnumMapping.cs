using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A C-like upstream enum (every variant unit-shaped) mapped to explicit <c>i32</c> values.
/// </summary>
internal sealed class EnumMapping
{
    /// <summary>The type's path inside the crate, shortest public form without
    /// <c>crate::</c>.</summary>
    public string RustPath { get; set; }

    /// <summary>The Rust type name.</summary>
    public string RustTypeName { get; set; }

    /// <summary>The C name: <c>&lt;prefix&gt;&lt;snake name&gt;</c> (the values travel as
    /// <c>int32_t</c>).</summary>
    public string CName { get; set; }

    /// <summary>The managed enum name (an <c>int</c> enum).</summary>
    public string ManagedName { get; set; }

    /// <summary>The type's Rust doc comments.</summary>
    public string Doc { get; set; }

    /// <summary>The variants in declaration order.</summary>
    public IList<EnumVariantMapping> Variants { get; set; } = new List<EnumVariantMapping>();
}
