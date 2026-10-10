using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Docker.Internal;

/// <summary>
/// Builds the labels the workbench puts on everything it creates, and the label filters that
/// find those resources again.
/// </summary>
internal sealed class ResourceLabels
{
    public const string KindImage = "image";
    public const string KindContainer = "container";
    public const string KindCargoCache = "cargo-cache";
    public const string KindTarget = "target";

    public ResourceLabels(string prefix, string workbenchId)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("The label prefix cannot be blank.", nameof(prefix));
        }

        Prefix = prefix.Trim();
        WorkbenchId = workbenchId ?? string.Empty;
    }

    public string Prefix { get; }

    public string WorkbenchId { get; }

    public string WorkbenchKey => Prefix + ".workbench";

    public string KindKey => Prefix + ".kind";

    public string SpecHashKey => Prefix + ".spec-hash";

    public string ReuseKeyKey => Prefix + ".reuse-key";

    public Dictionary<string, string> For(string kind)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Prefix] = "1",
            [WorkbenchKey] = WorkbenchId,
            [KindKey] = kind,
        };

    public Dictionary<string, string> PresenceFilter()
        => new Dictionary<string, string>(StringComparer.Ordinal) { [Prefix] = "1" };

    public Dictionary<string, string> WorkbenchFilter()
        => new Dictionary<string, string>(StringComparer.Ordinal) { [Prefix] = "1", [WorkbenchKey] = WorkbenchId };
}
