namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// One C parameter of an <see cref="ExportedFunction"/>.
/// </summary>
internal sealed class ParameterMapping
{
    /// <summary>The Rust parameter it comes from (shared by the two halves of a slice, string or
    /// optional): its name, <c>self</c> for the receiver, <c>return</c> for out-parameters.</summary>
    public string RustName { get; set; }

    /// <summary>The C parameter name (also the facade's parameter name; never a C or Rust
    /// keyword; unique within the function).</summary>
    public string CName { get; set; }

    /// <summary>The managed parameter name (camelCase, C# keywords escaped with <c>@</c>).</summary>
    public string ManagedName { get; set; }

    /// <summary>The type: for data / value / out roles the carried type, for length roles
    /// <c>usize</c>, for present flags <c>bool</c>.</summary>
    public TypeMapping Type { get; set; }

    /// <summary>What the parameter carries.</summary>
    public ParameterRole Role { get; set; }
}
