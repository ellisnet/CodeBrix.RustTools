using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// The upstream result of an <see cref="ExportedFunction"/>.
/// </summary>
internal sealed class ReturnMapping
{
    /// <summary>The carried type: the value / handle / buffer / string type, the inner type of
    /// an <c>Option</c>, the <c>Ok</c> type of a <c>Result</c> (unit for
    /// <see cref="ReturnShape.ResultVoid"/>), unit for <see cref="ReturnShape.Void"/>;
    /// <c>null</c> for <see cref="ReturnShape.Tuple"/> and <see cref="ReturnShape.ResultTuple"/>.</summary>
    public TypeMapping Type { get; set; }

    /// <summary>The shape.</summary>
    public ReturnShape Shape { get; set; }

    /// <summary>The tuple elements of <see cref="ReturnShape.Tuple"/> /
    /// <see cref="ReturnShape.ResultTuple"/> (empty otherwise).</summary>
    public IList<TypeMapping> TupleItems { get; set; } = new List<TypeMapping>();

    /// <summary>The error type of a <c>Result</c> as written (<c>ParseError</c>); <c>null</c>
    /// otherwise.</summary>
    public string ErrorTypeDisplay { get; set; }

    /// <summary>ADDED (WP6): how the facade turns the error into the last-error message:
    /// <c>"Display"</c> (<c>to_string()</c>), <c>"Debug"</c> (<c>format!("{:?}")</c>, when no
    /// Display implementation is known; a plan warning says so) or <c>"TypeName"</c> (neither is
    /// known: the message is the error type's name); <c>null</c> when not a Result.</summary>
    public string ErrorFormat { get; set; }
}
