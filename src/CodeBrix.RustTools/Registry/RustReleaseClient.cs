using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// Reads the latest Rust and rustup release versions from the official, keyless release
/// manifests on static.rust-lang.org.
/// </summary>
public sealed class RustReleaseClient
{
    private readonly RustWorkbenchOptions _options;
    private readonly HttpClientSource _http;

    /// <summary>
    /// Creates a client that uses the HTTP settings and base address in
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Library settings, or <c>null</c> for the defaults.</param>
    public RustReleaseClient(RustWorkbenchOptions options = null)
    {
        _options = options ?? new RustWorkbenchOptions();
        _http = new HttpClientSource(_options);
    }

    /// <summary>
    /// Downloads and parses the stable channel manifest.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="RustToolsException">The manifest could not be downloaded or parsed.</exception>
    public Task<RustReleaseInfo> GetLatestStableReleaseAsync(CancellationToken cancellationToken = default)
        => GetChannelReleaseAsync(RustChannel.Stable, cancellationToken);

    /// <summary>
    /// Downloads and parses the manifest of a release channel.
    /// </summary>
    /// <param name="channel"><see cref="RustChannel.Stable"/>, <see cref="RustChannel.Beta"/> or
    /// <see cref="RustChannel.Nightly"/>.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="ArgumentOutOfRangeException">Another channel was requested.</exception>
    /// <exception cref="RustToolsException">The manifest could not be downloaded or parsed.</exception>
    public async Task<RustReleaseInfo> GetChannelReleaseAsync(RustChannel channel, CancellationToken cancellationToken = default)
    {
        var name = channel switch
        {
            RustChannel.Stable => "stable",
            RustChannel.Beta => "beta",
            RustChannel.Nightly => "nightly",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Only the stable, beta and nightly channels have manifests."),
        };
        var uri = new Uri(_options.RustDistributionBaseUri, $"dist/channel-rust-{name}.toml");
        var text = await _http.GetStringAsync(uri, null, cancellationToken).ConfigureAwait(false);
        return ParseChannelManifest(text, name);
    }

    /// <summary>
    /// Downloads the rustup release manifest and returns the latest rustup version.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="RustToolsException">The manifest could not be downloaded or parsed.</exception>
    public async Task<ToolVersion> GetLatestRustupVersionAsync(CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_options.RustDistributionBaseUri, "rustup/release-stable.toml");
        var text = await _http.GetStringAsync(uri, null, cancellationToken).ConfigureAwait(false);
        return ParseRustupManifest(text);
    }

    /// <summary>
    /// Parses a channel manifest (<c>[pkg.rustc] version = "1.94.1 (e408947bf 2026-03-25)"</c>,
    /// <c>[pkg.cargo]</c>, <c>[pkg.clippy-preview]</c>, <c>[pkg.rustfmt-preview]</c>).
    /// </summary>
    internal static RustReleaseInfo ParseChannelManifest(string toml, string channel = "stable")
    {
        TomlTable document;
        try
        {
            document = TomlHelpers.Parse(toml, $"channel-rust-{channel}.toml");
        }
        catch (RustToolsException ex)
        {
            throw new RustToolsException($"The {channel} channel manifest could not be parsed: {ex.Message}", ex);
        }

        var rustcText = TomlHelpers.GetString(document, "pkg", "rustc", "version");
        if (!ToolVersion.TryParse(rustcText, out var rustc))
        {
            throw new RustToolsException($"The {channel} channel manifest has no usable [pkg.rustc] version.");
        }

        DateOnly? date = null;
        var dateValue = TomlHelpers.GetValue(document, "date");
        if (dateValue is string dateText
            && DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }
        else if (dateValue is TomlDateTime tomlDate)
        {
            date = DateOnly.FromDateTime(tomlDate.DateTime.DateTime);
        }

        return new RustReleaseInfo
        {
            Channel = channel,
            Date = date,
            RustcVersion = rustc,
            CargoPackageVersion = VersionOrNull(TomlHelpers.GetString(document, "pkg", "cargo", "version")),
            ClippyVersion = VersionOrNull(TomlHelpers.GetString(document, "pkg", "clippy-preview", "version")
                ?? TomlHelpers.GetString(document, "pkg", "clippy", "version")),
            RustfmtVersion = VersionOrNull(TomlHelpers.GetString(document, "pkg", "rustfmt-preview", "version")
                ?? TomlHelpers.GetString(document, "pkg", "rustfmt", "version")),
        };
    }

    /// <summary>
    /// Parses the rustup release manifest (<c>version = '1.29.1'</c>).
    /// </summary>
    internal static ToolVersion ParseRustupManifest(string toml)
    {
        var document = TomlHelpers.Parse(toml, "release-stable.toml");
        var text = TomlHelpers.GetString(document, "version");
        if (!ToolVersion.TryParse(text, out var version))
        {
            throw new RustToolsException("The rustup release manifest has no usable version.");
        }

        return version;
    }

    private static ToolVersion VersionOrNull(string text) => ToolVersion.TryParse(text, out var version) ? version : null;
}
