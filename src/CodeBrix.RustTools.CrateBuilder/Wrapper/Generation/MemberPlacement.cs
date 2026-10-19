namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// Where a generated managed member lives.
/// </summary>
internal enum MemberPlacement
{
    /// <summary>A static method of the library class.</summary>
    FreeFunction,

    /// <summary>An instance method of a mirror (the Rust function takes the mirror as
    /// <c>self</c>).</summary>
    MirrorInstance,

    /// <summary>A static method of a mirror.</summary>
    MirrorStatic,

    /// <summary>An instance method of a handle class.</summary>
    HandleMethod,

    /// <summary>A static factory method of a handle class.</summary>
    HandleFactory,

    /// <summary>The private helper behind a handle class constructor: returns the raw
    /// native handle.</summary>
    HandleConstructorHelper,

    /// <summary>Pass-through mode: a static method of the library class that forwards to
    /// the crate's own C export.</summary>
    PassThrough,
}
