using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// The member names already used in one generated type; hands out a unique name for each
/// new member (a name taken by a property, the type itself or an inherited member gets a
/// <c>Get</c> prefix, then a number).
/// </summary>
internal sealed class NameScope
{
    private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Creates a scope with <paramref name="reserved"/> taken.</summary>
    public NameScope(IEnumerable<string> reserved)
    {
        foreach (var name in reserved)
        {
            _used.Add(name);
        }
    }

    /// <summary>Marks a name as taken.</summary>
    public void Reserve(string name) => _used.Add(name);

    /// <summary>Whether a name is taken.</summary>
    public bool IsUsed(string name) => _used.Contains(name);

    /// <summary>A unique name based on <paramref name="name"/>, marked as taken.</summary>
    public string Claim(string name)
    {
        var candidate = name;
        if (_used.Contains(candidate) && !candidate.StartsWith("Get", StringComparison.Ordinal))
        {
            candidate = "Get" + name;
        }

        var stem = candidate;
        for (var n = 2; _used.Contains(candidate); n++)
        {
            candidate = stem + n.ToString(CultureInfo.InvariantCulture);
        }

        _used.Add(candidate);
        return candidate;
    }
}
