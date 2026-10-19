using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// One generated type in its own file: the name and the ready-indented member lines the
/// template places inside the declaration.
/// </summary>
internal sealed class TypeFileModel
{
    /// <summary>The type name (also the file name).</summary>
    public string Name { get; set; }

    /// <summary>The XML doc lines of the type, each with its <c>///</c> prefix.</summary>
    public List<string> DocLines { get; set; } = new List<string>();

    /// <summary>The member lines.</summary>
    public List<string> BodyLines { get; set; } = new List<string>();

    /// <summary>Handles: the SafeHandle class name.</summary>
    public string SafeHandleName { get; set; }

    /// <summary>Handles: whether a method consumes the object.</summary>
    public bool HasConsumingMethod { get; set; }

    /// <summary>Handles: the destroy function.</summary>
    public string DestroyFunction { get; set; }

    /// <summary>Mirrors: the blittable native struct (qualified).</summary>
    public string NativeName { get; set; }

    /// <summary>Enums: the underlying type.</summary>
    public string UnderlyingType { get; set; }

    /// <summary>Inline arrays: the length.</summary>
    public int Length { get; set; }

    /// <summary>Inline arrays: the element type.</summary>
    public string ElementType { get; set; }
}
