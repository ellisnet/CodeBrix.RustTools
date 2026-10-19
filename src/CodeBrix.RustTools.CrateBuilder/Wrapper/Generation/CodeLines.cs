using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// Collects generated C# lines with four-space indentation; an empty line stays empty (no
/// trailing whitespace).
/// </summary>
internal sealed class CodeLines
{
    private readonly List<string> _lines = new List<string>();
    private int _indent;

    /// <summary>Creates a collector starting at <paramref name="indent"/> levels.</summary>
    public CodeLines(int indent)
    {
        _indent = indent;
    }

    /// <summary>The lines so far.</summary>
    public List<string> Lines => _lines;

    /// <summary>Adds one line at the current indentation.</summary>
    public void Line(string text) => _lines.Add(string.IsNullOrEmpty(text) ? string.Empty : new string(' ', _indent * 4) + text);

    /// <summary>Adds every XML doc line with the <c>///</c> prefix.</summary>
    public void Doc(IEnumerable<string> docLines)
    {
        foreach (var line in docLines)
        {
            Line("/// " + line);
        }
    }

    /// <summary>Adds <c>{</c> and indents.</summary>
    public void Open()
    {
        Line("{");
        _indent++;
    }

    /// <summary>Outdents and adds <c>}</c> (with an optional suffix such as <c>;</c>).</summary>
    public void Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
    }
}
