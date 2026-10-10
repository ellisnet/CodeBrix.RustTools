using System;
using System.Collections.Generic;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// Small, null-tolerant accessors over the CodeBrix.TomlParse document model (TomlTable).
/// </summary>
internal static class TomlHelpers
{
    /// <summary>
    /// Parses TOML text into a document table.
    /// </summary>
    /// <exception cref="RustToolsException">The text is not valid TOML; the inner exception is the
    /// parser's TomlException (with line and column).</exception>
    public static TomlTable Parse(string text, string sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var options = new TomlSerializerOptions { SourceName = sourceName };
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text, options);
        }
        catch (TomlException ex)
        {
            throw new RustToolsException($"Invalid TOML in {sourceName ?? "the supplied text"}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses TOML text, returning <c>false</c> instead of throwing when it is invalid.
    /// </summary>
    public static bool TryParse(string text, out TomlTable table)
    {
        table = null;
        if (text == null)
        {
            return false;
        }

        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text);
            return table != null;
        }
        catch (TomlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the value at a dotted path of table keys (each element is one key, so keys that
    /// contain dots are supported), or <c>null</c> when any step is missing or not a table.
    /// </summary>
    public static object GetValue(TomlTable table, params string[] path)
    {
        if (table == null || path == null || path.Length == 0)
        {
            return null;
        }

        object current = table;
        foreach (var key in path)
        {
            if (current is not TomlTable currentTable || !currentTable.TryGetValue(key, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Returns the sub-table at the path, or <c>null</c>.
    /// </summary>
    public static TomlTable GetTable(TomlTable table, params string[] path) => GetValue(table, path) as TomlTable;

    /// <summary>
    /// Returns the string at the path, or <c>null</c> when missing or not a string.
    /// </summary>
    public static string GetString(TomlTable table, params string[] path) => GetValue(table, path) as string;

    /// <summary>
    /// Returns the integer at the path, or <c>null</c> when missing or not an integer.
    /// </summary>
    public static long? GetInteger(TomlTable table, params string[] path) => GetValue(table, path) is long value ? value : null;

    /// <summary>
    /// Returns the boolean at the path, or <c>null</c> when missing or not a boolean.
    /// </summary>
    public static bool? GetBoolean(TomlTable table, params string[] path) => GetValue(table, path) is bool value ? value : null;

    /// <summary>
    /// Returns the array of tables at the path, or <c>null</c>.
    /// </summary>
    public static TomlTableArray GetTableArray(TomlTable table, params string[] path) => GetValue(table, path) as TomlTableArray;

    /// <summary>
    /// Returns the string elements of the array at the path (non-strings are skipped). A single
    /// string value is returned as a one-element list. Missing values give an empty list.
    /// </summary>
    public static IReadOnlyList<string> GetStringList(TomlTable table, params string[] path)
    {
        var value = GetValue(table, path);
        if (value is string single)
        {
            return new[] { single };
        }

        var result = new List<string>();
        if (value is TomlArray array)
        {
            foreach (var item in array)
            {
                if (item is string text)
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the keys of the table at the path, in document order; empty when missing.
    /// </summary>
    public static IReadOnlyList<string> GetKeys(TomlTable table, params string[] path)
    {
        var target = path == null || path.Length == 0 ? table : GetTable(table, path);
        return target == null ? Array.Empty<string>() : new List<string>(target.Keys);
    }
}
