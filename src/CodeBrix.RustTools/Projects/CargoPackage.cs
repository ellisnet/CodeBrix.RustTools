using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// The <c>[package]</c> table of a Cargo.toml, or the <c>[workspace.package]</c> table that
/// members inherit from. Fields written as <c>field.workspace = true</c> are listed in
/// <see cref="InheritedFields"/> and their values stay <c>null</c> until
/// <see cref="WithInheritance"/> fills them from the workspace.
/// </summary>
public sealed class CargoPackage
{
    /// <summary>The package name (<c>null</c> for <c>[workspace.package]</c>).</summary>
    public string Name { get; init; }

    /// <summary>The package version, or <c>null</c> (inherited or omitted).</summary>
    public string Version { get; init; }

    /// <summary>The Rust edition (<c>2021</c>, <c>2024</c>, ...), or <c>null</c>.</summary>
    public string Edition { get; init; }

    /// <summary>The description, or <c>null</c>.</summary>
    public string Description { get; init; }

    /// <summary>The SPDX license expression of the <c>license</c> field, or <c>null</c>.</summary>
    public string License { get; init; }

    /// <summary>The <c>license-file</c> path (relative to the package), or <c>null</c>.</summary>
    public string LicenseFile { get; init; }

    /// <summary>The repository URL, or <c>null</c>.</summary>
    public string Repository { get; init; }

    /// <summary>The home page URL, or <c>null</c>.</summary>
    public string Homepage { get; init; }

    /// <summary>The documentation URL, or <c>null</c>.</summary>
    public string Documentation { get; init; }

    /// <summary>The readme path, or <c>null</c>.</summary>
    public string Readme { get; init; }

    /// <summary>The minimum supported Rust version (<c>rust-version</c>), or <c>null</c>.</summary>
    public string RustVersion { get; init; }

    /// <summary>The <c>authors</c> list.</summary>
    public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();

    /// <summary>The <c>keywords</c> list.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>The <c>categories</c> list.</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>The <c>publish</c> flag: <c>false</c> for <c>publish = false</c> or an empty
    /// registry list, <c>true</c> when set to true or to a registry list, <c>null</c> when not
    /// set (cargo's default is to allow publishing).</summary>
    public bool? Publish { get; init; }

    /// <summary>The build script path from <c>build = "..."</c>, or <c>null</c>.</summary>
    public string BuildScript { get; init; }

    /// <summary>True when the package sets <c>build = false</c> (no build script even if
    /// <c>build.rs</c> exists).</summary>
    public bool BuildScriptDisabled { get; init; }

    /// <summary>The <c>links</c> value (native library this package links), or <c>null</c>.</summary>
    public string Links { get; init; }

    /// <summary>The <c>default-run</c> binary name, or <c>null</c>.</summary>
    public string DefaultRun { get; init; }

    /// <summary>The <c>autobins</c> flag, or <c>null</c> when not set (default on).</summary>
    public bool? AutoBins { get; init; }

    /// <summary>The <c>autoexamples</c> flag, or <c>null</c> when not set (default on).</summary>
    public bool? AutoExamples { get; init; }

    /// <summary>The <c>autotests</c> flag, or <c>null</c> when not set (default on).</summary>
    public bool? AutoTests { get; init; }

    /// <summary>The <c>autobenches</c> flag, or <c>null</c> when not set (default on).</summary>
    public bool? AutoBenches { get; init; }

    /// <summary>The Cargo.toml keys written as <c>key.workspace = true</c>
    /// (for example <c>version</c>, <c>edition</c>, <c>license</c>).</summary>
    public IReadOnlyList<string> InheritedFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when <paramref name="field"/> (a Cargo.toml key such as <c>license</c> or
    /// <c>rust-version</c>) is inherited from the workspace.
    /// </summary>
    /// <param name="field">The Cargo.toml key.</param>
    public bool IsInherited(string field) => InheritedFields.Contains(field, StringComparer.Ordinal);

    /// <summary>
    /// Returns a copy in which every inherited field takes its value from
    /// <paramref name="workspacePackage"/> (the workspace root's <c>[workspace.package]</c>).
    /// Fields that are not inherited keep their own value. The copy's
    /// <see cref="InheritedFields"/> list is unchanged so the origin stays visible.
    /// </summary>
    /// <param name="workspacePackage">The <c>[workspace.package]</c> table, or <c>null</c>
    /// (the package is returned unchanged).</param>
    public CargoPackage WithInheritance(CargoPackage workspacePackage)
    {
        if (workspacePackage == null || InheritedFields.Count == 0)
        {
            return this;
        }

        string Pick(string field, string own, string inherited) => IsInherited(field) ? inherited : own;
        IReadOnlyList<string> PickList(string field, IReadOnlyList<string> own, IReadOnlyList<string> inherited) => IsInherited(field) ? inherited : own;

        return new CargoPackage
        {
            Name = Name,
            Version = Pick("version", Version, workspacePackage.Version),
            Edition = Pick("edition", Edition, workspacePackage.Edition),
            Description = Pick("description", Description, workspacePackage.Description),
            License = Pick("license", License, workspacePackage.License),
            LicenseFile = Pick("license-file", LicenseFile, workspacePackage.LicenseFile),
            Repository = Pick("repository", Repository, workspacePackage.Repository),
            Homepage = Pick("homepage", Homepage, workspacePackage.Homepage),
            Documentation = Pick("documentation", Documentation, workspacePackage.Documentation),
            Readme = Pick("readme", Readme, workspacePackage.Readme),
            RustVersion = Pick("rust-version", RustVersion, workspacePackage.RustVersion),
            Authors = PickList("authors", Authors, workspacePackage.Authors),
            Keywords = PickList("keywords", Keywords, workspacePackage.Keywords),
            Categories = PickList("categories", Categories, workspacePackage.Categories),
            Publish = IsInherited("publish") ? workspacePackage.Publish : Publish,
            BuildScript = BuildScript,
            BuildScriptDisabled = BuildScriptDisabled,
            Links = Links,
            DefaultRun = DefaultRun,
            AutoBins = AutoBins,
            AutoExamples = AutoExamples,
            AutoTests = AutoTests,
            AutoBenches = AutoBenches,
            InheritedFields = InheritedFields,
        };
    }

    /// <inheritdoc />
    public override string ToString() => Version == null ? Name ?? "[workspace.package]" : $"{Name} v{Version}";
}
