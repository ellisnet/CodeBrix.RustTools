using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Converts the objects in cargo's <c>--message-format=json</c> output into
/// <see cref="CompilerDiagnostic"/> and <see cref="BuildArtifact"/> instances.
/// </summary>
internal static class CargoJsonMessageParser
{
    private static readonly Regex SummaryMessage = new Regex(
        @"^(aborting due to|\d+ warnings? emitted|.*; \d+ warnings? emitted$)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses a <c>compiler-message</c> object (the whole line's root element).
    /// </summary>
    public static CompilerDiagnostic ParseCompilerMessage(JsonElement root)
    {
        var packageId = GetString(root, "package_id");
        string targetName = null;
        var source = DiagnosticOrigin.Rustc;
        if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
        {
            targetName = GetString(target, "name");
            if (GetStringArray(target, "kind").Contains("custom-build"))
            {
                source = DiagnosticOrigin.BuildScript;
            }
        }

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseDiagnostic(message, packageId, PackageIdParser.Parse(packageId).Name, targetName, source);
    }

    /// <summary>
    /// Parses one rustc diagnostic object (the <c>message</c> member, or a child).
    /// </summary>
    public static CompilerDiagnostic ParseDiagnostic(JsonElement message, string packageId, string packageName, string targetName, DiagnosticOrigin source)
    {
        string code = null;
        if (message.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Object)
        {
            code = GetString(codeElement, "code");
        }

        var spans = new List<CodeSpan>();
        if (message.TryGetProperty("spans", out var spansElement) && spansElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var span in spansElement.EnumerateArray())
            {
                spans.Add(new CodeSpan
                {
                    FileName = GetString(span, "file_name") ?? string.Empty,
                    LineStart = GetInt(span, "line_start"),
                    ColumnStart = GetInt(span, "column_start"),
                    LineEnd = GetInt(span, "line_end"),
                    ColumnEnd = GetInt(span, "column_end"),
                    IsPrimary = GetBool(span, "is_primary"),
                    Label = GetString(span, "label"),
                    SuggestedReplacement = GetString(span, "suggested_replacement"),
                });
            }
        }

        var children = new List<CompilerDiagnostic>();
        if (message.TryGetProperty("children", out var childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenElement.EnumerateArray())
            {
                children.Add(ParseDiagnostic(child, packageId, packageName, targetName, source));
            }
        }

        return new CompilerDiagnostic
        {
            Level = ParseLevel(GetString(message, "level")),
            Code = code,
            Message = GetString(message, "message") ?? string.Empty,
            Rendered = GetString(message, "rendered") ?? string.Empty,
            Spans = spans,
            Children = children,
            PackageId = packageId,
            PackageName = packageName,
            TargetName = targetName,
            Source = source,
        };
    }

    /// <summary>
    /// Parses a <c>compiler-artifact</c> object.
    /// </summary>
    public static BuildArtifact ParseArtifact(JsonElement root)
    {
        var packageId = GetString(root, "package_id") ?? string.Empty;
        var (name, version) = PackageIdParser.Parse(packageId);
        string targetName = string.Empty;
        IReadOnlyList<string> kinds = Array.Empty<string>();
        if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
        {
            targetName = GetString(target, "name") ?? string.Empty;
            kinds = GetStringArray(target, "kind");
        }

        var profile = new ArtifactProfile();
        if (root.TryGetProperty("profile", out var profileElement) && profileElement.ValueKind == JsonValueKind.Object)
        {
            profile = new ArtifactProfile
            {
                OptLevel = GetScalarText(profileElement, "opt_level"),
                DebugInfo = GetScalarText(profileElement, "debuginfo"),
                DebugAssertions = GetBool(profileElement, "debug_assertions"),
                OverflowChecks = GetBool(profileElement, "overflow_checks"),
                Test = GetBool(profileElement, "test"),
            };
        }

        return new BuildArtifact
        {
            PackageId = packageId,
            PackageName = name ?? string.Empty,
            PackageVersion = version,
            ManifestPath = GetString(root, "manifest_path"),
            TargetName = targetName,
            Kinds = kinds,
            Features = GetStringArray(root, "features"),
            Executable = GetString(root, "executable"),
            Filenames = GetStringArray(root, "filenames"),
            Profile = profile,
            Fresh = GetBool(root, "fresh"),
        };
    }

    /// <summary>
    /// Maps rustc's level text to <see cref="DiagnosticLevel"/>.
    /// </summary>
    public static DiagnosticLevel ParseLevel(string level) => level switch
    {
        "error" => DiagnosticLevel.Error,
        "warning" => DiagnosticLevel.Warning,
        "note" => DiagnosticLevel.Note,
        "help" => DiagnosticLevel.Help,
        "failure-note" => DiagnosticLevel.FailureNote,
        _ when level != null && level.Contains("internal compiler error", StringComparison.OrdinalIgnoreCase) => DiagnosticLevel.Ice,
        _ when level != null && level.StartsWith("error", StringComparison.OrdinalIgnoreCase) => DiagnosticLevel.Error,
        _ when level != null && level.StartsWith("warning", StringComparison.OrdinalIgnoreCase) => DiagnosticLevel.Warning,
        _ => DiagnosticLevel.Note,
    };

    /// <summary>
    /// True for rustc's end-of-crate summaries ("aborting due to 2 previous errors",
    /// "3 warnings emitted"), which are not diagnostics of their own.
    /// </summary>
    public static bool IsSummary(CompilerDiagnostic diagnostic)
        => diagnostic.Spans.Count == 0 && diagnostic.Code == null && SummaryMessage.IsMatch(diagnostic.Message);

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string GetScalarText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static bool GetBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .ToList();
    }

    internal static int ParseCount(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
