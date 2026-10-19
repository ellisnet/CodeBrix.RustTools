using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;
using CodeBrix.TomlParse.Parsing;
using CodeBrix.TomlParse.Syntax;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// Replaces <c>path = "..."</c> values in Cargo.toml text through TomlParse's lossless syntax
/// tree, so that comments, blank lines, key order and formatting survive and only the string
/// token of each rewritten value changes. Handles every way TOML can spell the entry: an
/// inline table (<c>gamma = { path = "..." }</c>), a table header
/// (<c>[dependencies.gamma]</c>), and dotted keys (<c>gamma.path = "..."</c>).
/// </summary>
internal static class ManifestPathRewriter
{
    /// <summary>
    /// Returns <paramref name="text"/> with the <c>path</c> value of each entry in
    /// <paramref name="newPaths"/> (keyed by the entry's table keys, see
    /// <see cref="ManifestDependencyEntry.TableKeys"/>) replaced.
    /// </summary>
    /// <exception cref="InvalidDataException">The text does not parse, an entry's
    /// <c>path</c> value is not found, or the rewritten text does not read back with the new
    /// values.</exception>
    public static string Rewrite(string text, IReadOnlyList<KeyValuePair<IReadOnlyList<string>, string>> newPaths, string sourceName)
    {
        var document = SyntaxParser.Parse(text, sourceName);
        if (document.HasErrors)
        {
            throw new InvalidDataException($"{sourceName} cannot be parsed for rewriting: " + string.Join("; ", document.Diagnostics.Select(d => d.ToString())));
        }

        var pending = newPaths.ToList();
        var replaced = new bool[pending.Count];
        foreach (var keyValue in document.KeyValues)
        {
            Visit(keyValue, Array.Empty<string>(), pending, replaced);
        }

        foreach (var table in document.Tables)
        {
            if (table is TableSyntax)
            {
                var prefix = Segments(table.Name);
                foreach (var keyValue in table.Items)
                {
                    Visit(keyValue, prefix, pending, replaced);
                }
            }
        }

        for (var i = 0; i < pending.Count; i++)
        {
            if (!replaced[i])
            {
                throw new InvalidDataException($"{sourceName}: the path of [{string.Join(".", pending[i].Key)}] was not found in the syntax tree.");
            }
        }

        var result = document.ToString();
        Verify(result, pending, sourceName);
        return result;
    }

    /// <summary>The TOML text of a basic string holding <paramref name="value"/>.</summary>
    public static string BasicString(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static void Visit(KeyValueSyntax keyValue, IReadOnlyList<string> prefix, List<KeyValuePair<IReadOnlyList<string>, string>> pending, bool[] replaced)
    {
        var keys = prefix.Concat(Segments(keyValue.Key)).ToArray();
        if (keyValue.Value is InlineTableSyntax inline)
        {
            foreach (var item in inline.Items)
            {
                if (item.KeyValue != null)
                {
                    Visit(item.KeyValue, keys, pending, replaced);
                }
            }

            return;
        }

        if (keyValue.Value is not StringValueSyntax value || keys.Length < 2 || keys[keys.Length - 1] != "path")
        {
            return;
        }

        var tableKeys = keys.Take(keys.Length - 1).ToArray();
        for (var i = 0; i < pending.Count; i++)
        {
            if (!replaced[i] && ManifestDependencyScanner.SameKeys(pending[i].Key, tableKeys))
            {
                Replace(value, pending[i].Value);
                replaced[i] = true;
            }
        }
    }

    private static void Replace(StringValueSyntax value, string newValue)
    {
        var old = value.Token;
        var literal = old != null && old.TokenKind == TokenKind.StringLiteral && newValue.IndexOf('\'') < 0 && !newValue.Any(char.IsControl);
        value.Token = new SyntaxToken(literal ? TokenKind.StringLiteral : TokenKind.String, literal ? "'" + newValue + "'" : BasicString(newValue))
        {
            LeadingTrivia = old?.LeadingTrivia,
            TrailingTrivia = old?.TrailingTrivia,
        };
        value.Value = newValue;
    }

    private static IReadOnlyList<string> Segments(KeySyntax key)
    {
        var result = new List<string>();
        if (key == null)
        {
            return result;
        }

        result.Add(Segment(key.Key));
        if (key.DotKeys != null)
        {
            foreach (var dotted in key.DotKeys)
            {
                result.Add(Segment(dotted.Key));
            }
        }

        return result;
    }

    private static string Segment(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax quoted => quoted.Value,
        _ => key?.ToString().Trim(),
    };

    private static void Verify(string text, List<KeyValuePair<IReadOnlyList<string>, string>> expected, string sourceName)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text, new TomlSerializerOptions { SourceName = sourceName });
        }
        catch (TomlException ex)
        {
            throw new InvalidDataException($"{sourceName}: the rewritten manifest is not valid TOML: {ex.Message}", ex);
        }

        foreach (var pair in expected)
        {
            object current = table;
            foreach (var key in pair.Key.Append("path"))
            {
                current = current is TomlTable t && t.TryGetValue(key, out var next) ? next : null;
            }

            if (!string.Equals(current as string, pair.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{sourceName}: the rewritten path of [{string.Join(".", pair.Key)}] reads back as '{current}', not '{pair.Value}'.");
            }
        }
    }
}
