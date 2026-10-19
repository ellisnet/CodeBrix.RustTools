namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// What one C parameter carries.
/// </summary>
internal enum ParameterRole
{
    /// <summary>A value: primitive, bool, enum, mirror or fixed array (by value).</summary>
    Value,

    /// <summary>A handle pointer (the receiver <c>handle</c>, or a handle parameter).</summary>
    Handle,

    /// <summary>The data pointer of a slice (<c>const T*</c>, or <c>T*</c> when mutable).</summary>
    SliceData,

    /// <summary>The element count of the slice before it (<c>size_t</c>).</summary>
    SliceLength,

    /// <summary>The UTF-8 bytes of a string (<c>const uint8_t*</c>, not NUL-terminated).</summary>
    StringData,

    /// <summary>The byte count of the string before it (<c>size_t</c>).</summary>
    StringLength,

    /// <summary>An out-pointer receiving a value or a handle.</summary>
    OutValue,

    /// <summary>An out-pointer receiving whether an optional result is present (<c>bool</c>).</summary>
    OutPresent,

    /// <summary>An out-pointer receiving an owned buffer (the facade's Buffer: data, len, cap),
    /// released with the plan's Release function.</summary>
    OutBuffer,

    /// <summary>An out-pointer receiving a length (reserved; tier 1 does not use it).</summary>
    OutLength,

    /// <summary>ADDED (WP6): the <c>bool</c> "is present" flag of an optional INPUT parameter,
    /// followed by the value (or handle) parameter itself (<c>has_x</c>, <c>x</c>).</summary>
    InPresent,
}
