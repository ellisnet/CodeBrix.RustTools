using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Model;
using CodeBrix.Templating;
using CodeBrix.Templating.Runtime;
using CodeBrix.Templating.Syntax;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Rendering;

/// <summary>
/// The wrapper's Scriban templates (embedded resources) and the rendering rules: a fresh
/// <see cref="TemplateContext"/> per render, strict variables, no relaxed member access, every
/// limit set to throw instead of truncating, <c>\n</c> line endings. Parse errors and render
/// errors become <see cref="WrapperGenerationException"/>.
/// </summary>
internal static class WrapperTemplates
{
    /// <summary>The prefix of the templates' manifest resource names.</summary>
    public const string ResourcePrefix = "CodeBrix.RustTools.CrateBuilder.Wrapper.Templates.";

    /// <summary>The csproj template.</summary>
    public const string Project = "Project.csproj.sbn";

    /// <summary>Native/AssemblyInfo.cs.</summary>
    public const string AssemblyInfo = "AssemblyInfo.cs.sbn";

    /// <summary>Native/&lt;Lib&gt;Native.cs.</summary>
    public const string Native = "Native.cs.sbn";

    /// <summary>&lt;Lib&gt;Exception.cs.</summary>
    public const string Exception = "Exception.cs.sbn";

    /// <summary>&lt;Lib&gt;.cs.</summary>
    public const string Library = "Library.cs.sbn";

    /// <summary>Mirrors/&lt;Name&gt;.cs.</summary>
    public const string Mirror = "Mirror.cs.sbn";

    /// <summary>Arrays/&lt;Name&gt;.cs.</summary>
    public const string InlineArray = "InlineArray.cs.sbn";

    /// <summary>Enums/&lt;Name&gt;.cs.</summary>
    public const string Enum = "Enum.cs.sbn";

    /// <summary>&lt;Name&gt;.cs per handle.</summary>
    public const string Handle = "Handle.cs.sbn";

    /// <summary>README.md.</summary>
    public const string Readme = "README.md.sbn";

    /// <summary>AGENT-README.txt.</summary>
    public const string AgentReadme = "AGENT-README.txt.sbn";

    /// <summary>THIRD-PARTY-NOTICES.txt.</summary>
    public const string Notices = "THIRD-PARTY-NOTICES.txt.sbn";

    /// <summary>Tests/&lt;Lib&gt;SmokeTests.cs.</summary>
    public const string SmokeTests = "SmokeTests.cs.sbn";

    /// <summary>WRAP-REPORT.txt when the facade stage wrote none.</summary>
    public const string WrapReport = "WRAP-REPORT.txt.sbn";

    private static readonly ConcurrentDictionary<string, Template> Parsed = new ConcurrentDictionary<string, Template>(StringComparer.Ordinal);

    /// <summary>The template names embedded in the assembly.</summary>
    public static IReadOnlyList<string> EmbeddedNames()
        => typeof(WrapperTemplates).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n.Substring(ResourcePrefix.Length))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>The text of one template.</summary>
    public static string Text(string name)
    {
        using var stream = typeof(WrapperTemplates).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new WrapperGenerationException($"The wrapper template '{name}' is not embedded in the CrateBuilder assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>Renders one template over <paramref name="model"/>, with <paramref name="item"/>
    /// as the per-file object (<c>item</c> in the template; the model itself when
    /// <c>null</c>).</summary>
    public static string Render(string name, WrapperModel model, object item, CancellationToken cancellationToken)
    {
        var template = Parsed.GetOrAdd(name, Parse);
        var context = new TemplateContext
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false,
            AutoIndent = false,
            LoopLimit = 0,
            RecursiveLimit = 0,
            LimitToString = 0,
            OutputLimit = 0,
            OnStringLimit = ScriptLimitBehavior.Throw,
            OnOutputLimit = ScriptLimitBehavior.Throw,
            NewLine = "\n",
            CancellationToken = cancellationToken,
        };
        var globals = new ScriptObject();
        globals["model"] = model;
        globals["item"] = item ?? model;
        context.PushGlobal(globals);
        try
        {
            return template.Render(context).Replace("\r\n", "\n");
        }
        catch (ScriptRuntimeException ex)
        {
            throw new WrapperGenerationException($"The wrapper template '{name}' failed to render: {ex.Message}", ex);
        }
    }

    private static Template Parse(string name)
    {
        var template = Template.Parse(Text(name), name);
        if (template.HasErrors)
        {
            throw new WrapperGenerationException($"The wrapper template '{name}' does not parse: " + string.Join("; ", template.Messages.Select(m => m.ToString())));
        }

        return template;
    }
}
