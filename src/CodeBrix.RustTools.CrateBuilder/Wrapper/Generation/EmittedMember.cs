using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// The output of <see cref="FunctionEmitter"/> for one export: the managed member, its
/// reference-doc signature, and the <c>LibraryImport</c> declaration.
/// </summary>
internal sealed class EmittedMember
{
    /// <summary>The managed member name.</summary>
    public string Name { get; set; }

    /// <summary>The managed member, ready-indented (XML docs included).</summary>
    public List<string> Lines { get; set; } = new List<string>();

    /// <summary>The signature for the API reference.</summary>
    public string ApiSignature { get; set; }

    /// <summary>The one-line summary for the API reference.</summary>
    public string Summary { get; set; }

    /// <summary>The managed parameter list ("Type name, Type name").</summary>
    public string ParameterList { get; set; }

    /// <summary>The managed argument list ("name, name").</summary>
    public string ArgumentList { get; set; }

    /// <summary>The C symbol.</summary>
    public string CName { get; set; }

    /// <summary>The <c>LibraryImport</c> declaration lines (indented for the native class).</summary>
    public List<string> NativeDeclaration { get; set; } = new List<string>();

    /// <summary>Whether the member takes no managed arguments.</summary>
    public bool IsParameterless { get; set; }
}
