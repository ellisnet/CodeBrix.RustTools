namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// The C conventions of every generated facade (the wrapper writer and the documentation quote
/// them). Every export except <c>&lt;cname&gt;_destroy</c>, <c>&lt;prefix&gt;free_buffer</c> and
/// <c>&lt;prefix&gt;free_string</c> returns an <c>int32_t</c> status; results come back through
/// out-parameters that are written only when the status is <see cref="StatusOk"/>. A status
/// other than <see cref="StatusOk"/> leaves a message for <c>&lt;prefix&gt;last_error_message</c>
/// on the calling thread.
/// </summary>
internal static class FacadeConventions
{
    /// <summary>The call succeeded; the out-parameters hold the result.</summary>
    public const int StatusOk = 0;

    /// <summary>A pointer argument that must not be null was null (a slice or string pointer
    /// may be null only when its length is 0).</summary>
    public const int StatusNullArgument = -1;

    /// <summary>An argument was invalid: not UTF-8, a length too large for the element type, a
    /// misaligned pointer, a bool other than 0 / 1, an enum value the enum does not have, the
    /// same object passed twice where one of them is changed, or a too-small buffer for
    /// <c>last_error_message</c>.</summary>
    public const int StatusInvalidArgument = -2;

    /// <summary>The upstream function returned an error (<c>Err</c>); the message is its
    /// Display text (or Debug text / type name, per the plan's ErrorFormat).</summary>
    public const int StatusUpstreamError = -3;

    /// <summary>The upstream code panicked (caught at the boundary); the message starts with
    /// "panic: ". Every object passed to that call is marked broken, and later calls on it
    /// return this status again; it can still be destroyed.</summary>
    public const int StatusPanic = -4;

    /// <summary>The object was used up by a consuming method (Rust <c>self</c>); it can only be
    /// destroyed.</summary>
    public const int StatusHandleConsumed = -5;

    /// <summary>The folder under the staged workspace that holds generated facade crates.</summary>
    public const string FacadeFolderName = ".codebrix-facade";

    /// <summary>The manifest of generated files written into the facade crate folder.</summary>
    public const string GeneratedFilesManifestName = "generated-files.json";

    /// <summary>The read-me written into the facade crate folder.</summary>
    public const string ReadmeFileName = "FACADE-README.txt";

    /// <summary>The suffix of the last-error export (<c>&lt;prefix&gt;last_error_message(buf,
    /// cap, written)</c>).</summary>
    public const string LastErrorMessageSuffix = "last_error_message";

    /// <summary>The suffix of the buffer release export.</summary>
    public const string FreeBufferSuffix = "free_buffer";

    /// <summary>The suffix of the string release export.</summary>
    public const string FreeStringSuffix = "free_string";

    /// <summary>The suffix of the C Buffer struct type (<c>&lt;prefix&gt;buffer</c>).</summary>
    public const string BufferTypeSuffix = "buffer";

    /// <summary>The dependency key the facade gives the upstream crate (<c>use upstream as
    /// up;</c>).</summary>
    public const string UpstreamDependencyName = "upstream";

    /// <summary>The local the facade binds the receiver to before the upstream call.</summary>
    public const string ReceiverLocal = "this";
}
