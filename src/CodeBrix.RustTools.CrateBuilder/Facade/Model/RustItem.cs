using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The fields every recorded item carries (SCHEMA.md "Item header"): functions, structs,
/// enums, type aliases, constants and traits.
/// </summary>
internal abstract class RustItem
{
    /// <summary>Unique in the document: <c>&lt;kind&gt;:&lt;canonical path&gt;</c>
    /// (<c>fn:crate::Vec2::dot</c>), <c>#2</c>, <c>#3</c> ... for repeats.</summary>
    public string Id { get; set; }

    /// <summary>The identifier.</summary>
    public string Name { get; set; }

    /// <summary>Where it is declared (<c>crate::time::Tick</c>; methods: owner path + name).</summary>
    public string CanonicalPath { get; set; }

    /// <summary>Every path by which code outside the crate can name it, shortest first; empty
    /// = not reachable from outside the crate.</summary>
    public List<string> PublicPaths { get; set; } = new List<string>();

    /// <summary>The declaring module's path (<c>crate::shapes</c>).</summary>
    public string Module { get; set; }

    /// <summary><c>pub</c> for every recorded item.</summary>
    public string Visibility { get; set; }

    /// <summary>Every <c>cfg</c> predicate that applies, outermost first, verbatim; all must
    /// hold.</summary>
    public List<string> Cfg { get; set; } = new List<string>();

    /// <summary>The doc comments (rustdoc-style joined; empty when none).</summary>
    public string Docs { get; set; } = string.Empty;

    /// <summary><c>#[doc(hidden)]</c> is present.</summary>
    public bool IsDocHidden { get; set; }

    /// <summary>Other attributes, verbatim (<c>#[inline]</c>, <c>#[unsafe(no_mangle)]</c>).</summary>
    public List<string> Attributes { get; set; } = new List<string>();

    /// <summary>The source file, relative to the package folder.</summary>
    public string File { get; set; }

    /// <summary>The 1-based line of the item's name.</summary>
    public int Line { get; set; }

    /// <summary>Whether the item is declared <c>pub</c> (true for every recorded item).</summary>
    [JsonIgnore]
    public bool IsPublic => string.Equals(Visibility, "pub", StringComparison.Ordinal);

    /// <summary>Whether code outside the crate can name it (<see cref="PublicPaths"/> is not
    /// empty).</summary>
    [JsonIgnore]
    public bool IsReachable => PublicPaths.Count > 0;

    /// <summary>The shortest public path, or <c>null</c> when the item is not reachable.</summary>
    [JsonIgnore]
    public string PrimaryPublicPath => PublicPaths.FirstOrDefault();

    /// <summary>Whether a <c>cfg</c> predicate applies.</summary>
    [JsonIgnore]
    public bool IsConditional => Cfg.Count > 0;

    /// <summary>Whether a <c>cfg</c> predicate mentions <paramref name="text"/> (for example
    /// <c>"wasm32"</c> or <c>"test"</c>).</summary>
    public bool CfgMentions(string text) => Cfg.Any(c => c.Contains(text, StringComparison.Ordinal));

    /// <summary>Whether the verbatim attributes include one starting with
    /// <paramref name="prefix"/> (for example <c>"#[deprecated"</c>).</summary>
    public bool HasAttribute(string prefix) => Attributes.Any(a => a.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The id.</summary>
    public override string ToString() => Id ?? CanonicalPath ?? Name;
}
