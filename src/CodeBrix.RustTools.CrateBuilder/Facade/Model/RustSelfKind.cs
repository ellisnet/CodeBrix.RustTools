namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The receiver of a function (JSON: snake_case).
/// </summary>
internal enum RustSelfKind
{
    /// <summary>No receiver: a free function or an associated function such as <c>new</c>.</summary>
    None,

    /// <summary><c>self</c>.</summary>
    Value,

    /// <summary><c>mut self</c>.</summary>
    MutValue,

    /// <summary><c>&amp;self</c>.</summary>
    Ref,

    /// <summary><c>&amp;mut self</c>.</summary>
    RefMut,

    /// <summary>A typed receiver such as <c>self: Box&lt;Self&gt;</c>
    /// (<see cref="RustFunction.SelfType"/>).</summary>
    Typed,
}
