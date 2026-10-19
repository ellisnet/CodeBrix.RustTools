using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A non-<c>Copy</c> upstream type wrapped as an opaque handle: the facade boxes the value
/// (<c>Box::into_raw</c>) and the wrapper holds the pointer in a disposable class.
/// </summary>
internal sealed class HandleType
{
    /// <summary>The type's path inside the crate, shortest public form without <c>crate::</c>
    /// (<c>Encoder</c>, <c>codec::Encoder</c>).</summary>
    public string RustPath { get; set; }

    /// <summary>The Rust type name (<c>Encoder</c>).</summary>
    public string RustTypeName { get; set; }

    /// <summary>The C name of the opaque type: <c>&lt;prefix&gt;&lt;snake name&gt;</c>
    /// (<c>datacrate_encoder</c>).</summary>
    public string CName { get; set; }

    /// <summary>The wrapper class name.</summary>
    public string ManagedName { get; set; }

    /// <summary>Always false: a <c>Copy</c> type is a mirror, never a handle.</summary>
    public bool IsCopy { get; set; }

    /// <summary>The type implements <c>Default</c>: a <c>&lt;cname&gt;_default</c> constructor
    /// is generated.</summary>
    public bool HasDefault { get; set; }

    /// <summary>The type implements <c>Clone</c>: a <c>&lt;cname&gt;_clone</c> method is
    /// generated.</summary>
    public bool HasClone { get; set; }

    /// <summary>The type's Rust doc comments.</summary>
    public string Doc { get; set; }

    /// <summary>Every function whose <see cref="ExportedFunction.Owner"/> is this handle
    /// (constructors, default, methods, clone and <see cref="Destroy"/>), the same objects as in
    /// <see cref="MappingPlan.Functions"/> and in that order.</summary>
    public IList<ExportedFunction> Methods { get; set; } = new List<ExportedFunction>();

    /// <summary>The generated <c>&lt;cname&gt;_destroy(handle)</c> (always present).</summary>
    public ExportedFunction Destroy { get; set; }
}
