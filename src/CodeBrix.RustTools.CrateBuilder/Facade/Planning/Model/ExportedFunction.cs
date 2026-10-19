using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// One C export of the facade (or, in pass-through mode, of the upstream crate).
/// </summary>
/// <remarks>
/// C convention: when <see cref="ReturnsStatus"/> is true the export returns an <c>int32_t</c>
/// status (0 = success) and every result travels through the <c>Out*</c> parameters, which are
/// pointers to <see cref="ParameterMapping.Type"/>'s C type and are written only on success.
/// Destroy and Release functions return nothing; pass-through functions return their own C type.
/// </remarks>
internal sealed class ExportedFunction
{
    /// <summary>The upstream path inside the crate, shortest public form without
    /// <c>crate::</c> (<c>Vec2::dot</c>, <c>checksum</c>); generated functions use the owner's
    /// path plus their role (<c>Encoder::destroy</c>, <c>Encoder::default</c>,
    /// <c>Encoder::clone</c>, <c>free_buffer</c>).</summary>
    public string RustPath { get; set; }

    /// <summary>The exported C symbol.</summary>
    public string CName { get; set; }

    /// <summary>The managed member name.</summary>
    public string ManagedName { get; set; }

    /// <summary>The upstream Rust doc comments (empty for generated functions).</summary>
    public string Doc { get; set; }

    /// <summary>What the function is.</summary>
    public FunctionKind Kind { get; set; }

    /// <summary>The handle type it belongs to; <c>null</c> for <see cref="FunctionKind.Free"/>,
    /// <see cref="FunctionKind.Release"/> and <see cref="FunctionKind.PassThrough"/>.</summary>
    public HandleType Owner { get; set; }

    /// <summary>The C parameters in order (a Rust parameter can become two: data + length, or
    /// present flag + value), the receiver first, the out-parameters last.</summary>
    public IList<ParameterMapping> Parameters { get; set; } = new List<ParameterMapping>();

    /// <summary>The Rust-level result shape.</summary>
    public ReturnMapping Return { get; set; }

    /// <summary>The upstream function is <c>unsafe fn</c> (only planned with
    /// <see cref="PlannerOptions.IncludeUnsafeFunctions"/>).</summary>
    public bool IsUnsafeUpstream { get; set; }

    /// <summary>How the facade invokes upstream, for example
    /// <c>upstream::Encoder::encode(this, input)</c>. Each argument is a local the facade binds
    /// before the call, named after the Rust parameter (<c>this</c> for the receiver) and holding
    /// the UPSTREAM-typed value: mirrors, enums and arrays already converted, <c>&amp;str</c> /
    /// slices as references, handle parameters as <c>&amp;T</c> / <c>&amp;mut T</c>, a consumed
    /// receiver as the owned value, an optional parameter as <c>Option&lt;...&gt;</c>. <c>null</c>
    /// for Destroy and Release (the facade's own code).</summary>
    public string RustCallExpression { get; set; }

    /// <summary>ADDED (WP6): the export returns an <c>int32_t</c> status (every kind except
    /// Destroy, Release and PassThrough).</summary>
    public bool ReturnsStatus { get; set; }

    /// <summary>ADDED (WP6): the path of the Rust type the function belongs to (handle, mirror
    /// or enum; <c>Vec2</c>), or <c>null</c> for a free function.</summary>
    public string OwnerRustPath { get; set; }
}
