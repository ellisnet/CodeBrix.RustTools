using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using CodeBrix.Templating;
using CodeBrix.Templating.Runtime;
using CodeBrix.Templating.Syntax;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// The facade template catalog (embedded resources <c>Facade/Rendering/Templates/*.sbn</c>) and
/// the render rules every template follows: a fresh <see cref="TemplateContext"/> per render,
/// strict variables, no relaxed member access, string and output limits that THROW instead of
/// truncating, "\n" line endings, the invariant culture, no <c>include</c> (no template loader).
/// A parsed template is cached; a template with parse errors is a
/// <see cref="FacadeGenerationException"/> that lists the messages.
/// </summary>
internal static class FacadeTemplates
{
    /// <summary>The manifest resource name prefix of the templates.</summary>
    public const string ResourcePrefix = "CodeBrix.RustTools.CrateBuilder.Facade.Rendering.Templates.";

    /// <summary>The Cargo.toml template.</summary>
    public const string CargoToml = "Cargo.toml.sbn";

    /// <summary>The src/lib.rs template.</summary>
    public const string LibRs = "lib.rs.sbn";

    /// <summary>The C header template.</summary>
    public const string Header = "header.h.sbn";

    /// <summary>The build.rs template (WebGPU-linked facades).</summary>
    public const string BuildRs = "build.rs.sbn";

    /// <summary>The FACADE-README.txt template.</summary>
    public const string Readme = "FACADE-README.txt.sbn";

    /// <summary>The largest single string conversion and the largest output (characters).</summary>
    public const int OutputLimit = 64 * 1024 * 1024;

    /// <summary>The loop budget of one render (shared by nested loops).</summary>
    public const int LoopLimit = 10_000_000;

    private static readonly ConcurrentDictionary<string, Template> Cache = new ConcurrentDictionary<string, Template>(StringComparer.Ordinal);

    /// <summary>The names of the embedded templates.</summary>
    public static string[] Names() => typeof(FacadeTemplates).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(n => n.Substring(ResourcePrefix.Length))
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The text of an embedded template ("\r\n" normalised to "\n").</summary>
    public static string Text(string name)
    {
        using var stream = typeof(FacadeTemplates).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new FacadeGenerationException($"The facade template '{name}' is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>Parses <paramref name="text"/>; parse errors become a
    /// <see cref="FacadeGenerationException"/>.</summary>
    public static Template Parse(string name, string text)
    {
        var template = Template.Parse(text, name);
        if (template.HasErrors)
        {
            throw new FacadeGenerationException($"The facade template '{name}' has errors: " + string.Join("; ", template.Messages.Select(m => m.ToString())));
        }

        return template;
    }

    /// <summary>The template name of a model member: snake_case, an upper-case run before a
    /// capitalised word split off ("CName" -> "c_name", "WebGpuModuleLines" ->
    /// "web_gpu_module_lines").</summary>
    public static string SnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string Rename(System.Reflection.MemberInfo member) => SnakeCase(member.Name);

    /// <summary>Renders the embedded template <paramref name="name"/> with <paramref name="model"/>.</summary>
    public static string Render(string name, object model)
        => Render(name, Cache.GetOrAdd(name, n => Parse(n, Text(n))), model);

    /// <summary>Renders a parsed template with <paramref name="model"/> under the catalog's
    /// rules.</summary>
    public static string Render(string name, Template template, object model)
    {
        var context = new TemplateContext
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false,
            LoopLimit = LoopLimit,
            LimitToString = OutputLimit,
            OutputLimit = OutputLimit,
            OnStringLimit = ScriptLimitBehavior.Throw,
            OnOutputLimit = ScriptLimitBehavior.Throw,
            NewLine = "\n",
            AutoIndent = false,
            MemberRenamer = Rename,
        };
        var globals = new ScriptObject();
        globals.Import(model, renamer: Rename);
        context.PushGlobal(globals);
        try
        {
            return template.Render(context).Replace("\r\n", "\n");
        }
        catch (ScriptRuntimeException e)
        {
            throw new FacadeGenerationException($"The facade template '{name}' failed to render: {e.Message}", e);
        }
    }
}
