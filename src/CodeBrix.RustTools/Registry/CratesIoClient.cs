using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// A small client for the public crates.io web API (crate summaries and version lists). Every
/// request carries a descriptive User-Agent, as crates.io requires, and requests from this
/// process are spaced by <see cref="RustWorkbenchOptions.CratesIoRequestInterval"/>.
/// </summary>
public sealed class CratesIoClient
{
    private static readonly SemaphoreSlim Throttle = new SemaphoreSlim(1, 1);
    private static DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    private readonly RustWorkbenchOptions _options;
    private readonly HttpClientSource _http;

    /// <summary>
    /// Creates a client that uses the HTTP settings, User-Agent and base address in
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Library settings, or <c>null</c> for the defaults.</param>
    public CratesIoClient(RustWorkbenchOptions options = null)
    {
        _options = options ?? new RustWorkbenchOptions();
        _http = new HttpClientSource(_options);
    }

    /// <summary>
    /// Gets a crate's summary and version history.
    /// </summary>
    /// <param name="crateName">The crate name (case-insensitive on crates.io).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The crate, or <c>null</c> when crates.io has no crate with that name.</returns>
    /// <exception cref="ArgumentException"><paramref name="crateName"/> is blank or contains
    /// characters a crate name cannot have.</exception>
    /// <exception cref="RustToolsException">The request failed, was rejected, or returned an
    /// unexpected document.</exception>
    public async Task<CrateInfo> GetCrateAsync(string crateName, CancellationToken cancellationToken = default)
    {
        ValidateCrateName(crateName);
        var uri = new Uri(_options.CratesIoApiBaseUri, "crates/" + Uri.EscapeDataString(crateName.Trim()));

        await WaitForTurnAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _http.GetAsync(uri, "application/json", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new RustToolsException($"crates.io returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for crate '{crateName}'.");
        }

        return ParseCrateResponse(body);
    }

    /// <summary>
    /// Gets the version a dependency update would move to (the highest stable version, or the
    /// highest pre-release when there is no stable one).
    /// </summary>
    /// <param name="crateName">The crate name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The version text, or <c>null</c> when the crate does not exist.</returns>
    /// <exception cref="RustToolsException">The request failed.</exception>
    public async Task<string> GetLatestVersionAsync(string crateName, CancellationToken cancellationToken = default)
        => (await GetCrateAsync(crateName, cancellationToken).ConfigureAwait(false))?.LatestVersion;

    /// <summary>
    /// Parses a <c>GET /api/v1/crates/{name}</c> response body.
    /// </summary>
    internal static CrateInfo ParseCrateResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("crate", out var crate) || crate.ValueKind != JsonValueKind.Object)
            {
                throw new RustToolsException("The crates.io response has no 'crate' object.");
            }

            var versions = new List<CrateVersionInfo>();
            if (root.TryGetProperty("versions", out var versionArray) && versionArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in versionArray.EnumerateArray())
                {
                    var number = GetString(item, "num") ?? string.Empty;
                    versions.Add(new CrateVersionInfo
                    {
                        Number = number,
                        Version = ToolVersion.TryParse(number, out var parsed) ? parsed : null,
                        IsYanked = item.TryGetProperty("yanked", out var yanked) && yanked.ValueKind == JsonValueKind.True,
                        License = GetString(item, "license"),
                        CreatedAt = GetDate(item, "created_at"),
                        RustVersion = GetString(item, "rust_version"),
                    });
                }
            }

            return new CrateInfo
            {
                Name = GetString(crate, "name") ?? GetString(crate, "id") ?? string.Empty,
                Description = GetString(crate, "description"),
                Repository = GetString(crate, "repository"),
                Homepage = GetString(crate, "homepage"),
                Documentation = GetString(crate, "documentation"),
                MaxStableVersion = GetString(crate, "max_stable_version"),
                NewestVersion = GetString(crate, "newest_version"),
                MaxVersion = GetString(crate, "max_version"),
                Downloads = crate.TryGetProperty("downloads", out var downloads) && downloads.TryGetInt64(out var count) ? count : 0,
                UpdatedAt = GetDate(crate, "updated_at"),
                Versions = versions,
            };
        }
        catch (JsonException ex)
        {
            throw new RustToolsException($"The crates.io response is not valid JSON: {ex.Message}", ex);
        }
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        var interval = _options.CratesIoRequestInterval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        await Throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequest + interval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            Throttle.Release();
        }
    }

    private static void ValidateCrateName(string crateName)
    {
        if (string.IsNullOrWhiteSpace(crateName))
        {
            throw new ArgumentException("A crate name is required.", nameof(crateName));
        }

        foreach (var c in crateName.Trim())
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
            {
                throw new ArgumentException($"'{crateName}' is not a valid crate name.", nameof(crateName));
            }
        }
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return text != null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }
}
