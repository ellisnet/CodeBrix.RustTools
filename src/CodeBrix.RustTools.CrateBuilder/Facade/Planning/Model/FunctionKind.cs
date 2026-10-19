namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// What an <see cref="ExportedFunction"/> is.
/// </summary>
internal enum FunctionKind
{
    /// <summary>A free function, or an associated / receiver function of a mirror or enum
    /// (then <see cref="ExportedFunction.OwnerRustPath"/> names the type and the receiver is the
    /// first, by-value parameter).</summary>
    Free,

    /// <summary>Creates a handle (an associated function of a handle type returning it, or the
    /// generated <c>_default</c>).</summary>
    Constructor,

    /// <summary>A <c>&amp;self</c> method of a handle (also the generated <c>_clone</c>).</summary>
    Method,

    /// <summary>A <c>&amp;mut self</c> method of a handle.</summary>
    MutMethod,

    /// <summary>A <c>self</c> / <c>mut self</c> method of a handle: the facade takes the value
    /// out of the handle, which stays valid only for <c>_destroy</c> afterwards.</summary>
    ConsumingMethod,

    /// <summary>The generated <c>&lt;cname&gt;_destroy(handle)</c>.</summary>
    Destroy,

    /// <summary>A generated buffer / string release function (<c>&lt;prefix&gt;free_buffer</c>,
    /// <c>&lt;prefix&gt;free_string</c>).</summary>
    Release,

    /// <summary>Pass-through mode: one of the upstream crate's own C exports.</summary>
    PassThrough,
}
