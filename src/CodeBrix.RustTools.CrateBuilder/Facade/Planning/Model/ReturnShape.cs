namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// The shape of an upstream result, which decides the out-parameters.
/// </summary>
internal enum ReturnShape
{
    /// <summary>Nothing.</summary>
    Void,

    /// <summary>A value (out: <c>out</c>).</summary>
    Value,

    /// <summary>A new handle the caller owns (out: <c>out</c>).</summary>
    Handle,

    /// <summary>A <c>Vec</c> of primitives (out: <c>out</c> buffer, release with free_buffer).</summary>
    OwnedBuffer,

    /// <summary>A <c>String</c> (out: <c>out</c> buffer of UTF-8 bytes, release with free_string).</summary>
    OwnedString,

    /// <summary><c>Option</c> of a value, string or buffer (outs: <c>out_present</c>, then
    /// <c>out</c>).</summary>
    OptionalValue,

    /// <summary><c>Option</c> of a handle (out: <c>out</c>, null = none).</summary>
    OptionalHandle,

    /// <summary><c>Result</c> of a value (status -3 + last error on <c>Err</c>).</summary>
    ResultValue,

    /// <summary><c>Result</c> of a handle.</summary>
    ResultHandle,

    /// <summary><c>Result&lt;(), E&gt;</c>.</summary>
    ResultVoid,

    /// <summary><c>Result</c> of a <c>Vec</c> of primitives.</summary>
    ResultBuffer,

    /// <summary><c>Result</c> of a <c>String</c>.</summary>
    ResultString,

    /// <summary>A tuple (outs: <c>out0</c>, <c>out1</c> ...; <see cref="ReturnMapping.TupleItems"/>).</summary>
    Tuple,

    /// <summary>ADDED (WP6): <c>Result</c> of a tuple (outs as <see cref="Tuple"/>).</summary>
    ResultTuple,
}
