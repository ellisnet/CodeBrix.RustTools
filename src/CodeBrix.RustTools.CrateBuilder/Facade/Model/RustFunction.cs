using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A free function, an inherent-impl method (with <see cref="Owner"/>) or a trait method
/// (inside <see cref="RustTrait.Methods"/>, with <see cref="HasDefault"/>).
/// </summary>
internal sealed class RustFunction : RustItem
{
    /// <summary>The impl block of a method; <c>null</c> for a free function.</summary>
    public RustOwner Owner { get; set; }

    /// <summary>The receiver.</summary>
    public RustSelfKind SelfKind { get; set; }

    /// <summary>The receiver type of a <see cref="RustSelfKind.Typed"/> receiver.</summary>
    public RustType SelfType { get; set; }

    /// <summary>The parameters after the receiver.</summary>
    public List<RustParameter> Params { get; set; } = new List<RustParameter>();

    /// <summary>The return type (unit when there is no <c>-&gt;</c>).</summary>
    public RustType ReturnType { get; set; }

    /// <summary>The function's own generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary>A type or const parameter on the function or (for a method) its impl block.</summary>
    public bool HasGenerics { get; set; }

    /// <summary><c>unsafe fn</c>.</summary>
    public bool IsUnsafe { get; set; }

    /// <summary><c>async fn</c>.</summary>
    public bool IsAsync { get; set; }

    /// <summary><c>const fn</c>.</summary>
    public bool IsConst { get; set; }

    /// <summary>The ABI of an <c>extern "..." fn</c>; <c>null</c> otherwise.</summary>
    public string Abi { get; set; }

    /// <summary>The signature verbatim (<c>pub fn find(xs: &amp;[i32], x: i32) -&gt; Option&lt;usize&gt;</c>).</summary>
    public string Signature { get; set; }

    /// <summary>Trait methods only: the trait gives a default body.</summary>
    public bool? HasDefault { get; set; }

    /// <summary>Whether this is a method (it has an <see cref="Owner"/>).</summary>
    [JsonIgnore]
    public bool IsMethod => Owner != null;

    /// <summary>Whether it takes <c>self</c> in any form.</summary>
    [JsonIgnore]
    public bool HasReceiver => SelfKind != RustSelfKind.None;

    /// <summary>Whether it is <c>extern "C"</c> (a C export when it is also unmangled).</summary>
    [JsonIgnore]
    public bool IsExternC => Abi == "C";

    /// <summary>Whether it is unmangled (<c>#[no_mangle]</c> or <c>#[unsafe(no_mangle)]</c>).</summary>
    [JsonIgnore]
    public bool IsNoMangle => HasAttribute("#[no_mangle]") || HasAttribute("#[unsafe(no_mangle)]");
}
