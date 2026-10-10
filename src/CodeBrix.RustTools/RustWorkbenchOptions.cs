using System;
using System.Collections.Generic;
using System.Net.Http;

namespace CodeBrix.RustTools;

/// <summary>
/// Settings shared by <see cref="RustWorkbench"/> and every client and runner in the library:
/// where to find the Rust tools, which environment variables to pass to them, how long to wait
/// for them, and how to reach the network services used for update checks.
/// </summary>
public sealed class RustWorkbenchOptions
{
    /// <summary>
    /// The User-Agent sent to crates.io when <see cref="UserAgent"/> is not set. crates.io
    /// rejects anonymous clients, so a descriptive value is always sent.
    /// </summary>
    public const string DefaultUserAgent = "CodeBrix.RustTools (https://github.com/ellisnet/CodeBrix.RustTools)";

    private TimeSpan? _toolchainCacheDuration;

    /// <summary>
    /// Full path of the <c>cargo</c> executable to use instead of searching PATH and the cargo
    /// home folder. <c>null</c> (the default) searches.
    /// </summary>
    public string CargoPath { get; set; }

    /// <summary>
    /// Full path of the <c>rustc</c> executable to use instead of searching. <c>null</c> (the
    /// default) searches.
    /// </summary>
    public string RustcPath { get; set; }

    /// <summary>
    /// Full path of the <c>rustup</c> executable to use instead of searching. <c>null</c> (the
    /// default) searches.
    /// </summary>
    public string RustupPath { get; set; }

    /// <summary>
    /// An Android NDK root to use instead of searching for one (see
    /// <see cref="Toolchain.AndroidNdkLocator"/>); <c>null</c> (the default) searches the
    /// <c>ANDROID_NDK_HOME</c>, <c>ANDROID_NDK_ROOT</c>, <c>ANDROID_HOME</c> and
    /// <c>ANDROID_SDK_ROOT</c> variables. A folder that is not an NDK for this machine is
    /// reported as no NDK, with no fallback.
    /// </summary>
    public string AndroidNdkPath { get; set; }

    /// <summary>
    /// The cargo home folder (the value of <c>CARGO_HOME</c>). When set it is searched for
    /// executables in its <c>bin</c> sub-folder and passed to every child process as
    /// <c>CARGO_HOME</c>. <c>null</c> (the default) uses the <c>CARGO_HOME</c> environment
    /// variable, or <c>~/.cargo</c>.
    /// </summary>
    public string CargoHome { get; set; }

    /// <summary>
    /// The rustup home folder (the value of <c>RUSTUP_HOME</c>). When set it is passed to every
    /// child process as <c>RUSTUP_HOME</c>. <c>null</c> (the default) uses the inherited
    /// environment.
    /// </summary>
    public string RustupHome { get; set; }

    /// <summary>
    /// Extra folders searched for tool executables, before PATH. Empty by default.
    /// </summary>
    public IList<string> AdditionalSearchDirectories { get; } = new List<string>();

    /// <summary>
    /// Environment variables added to (or, with a <c>null</c> value, removed from) the
    /// environment of every process the library starts. Empty by default.
    /// </summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// How long a single tool probe (for example <c>cargo --version</c>) may run before it is
    /// killed and reported as <see cref="Toolchain.ToolStatus.Unknown"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ToolProbeTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a <see cref="RustWorkbench"/> keeps serving a cached toolchain detection (see
    /// <see cref="RustWorkbench.DetectToolchainAsync"/>) before it detects again. <c>null</c>
    /// (the default) keeps a detection for the lifetime of the workbench;
    /// <see cref="TimeSpan.Zero"/> detects every time. Read on every lookup, so a change
    /// applies to entries already cached.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan? ToolchainCacheDuration
    {
        get => _toolchainCacheDuration;
        set
        {
            if (value.HasValue && value.Value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The toolchain cache duration cannot be negative.");
            }

            _toolchainCacheDuration = value;
        }
    }

    /// <summary>
    /// The <see cref="System.Net.Http.HttpClient"/> used for update checks and crates.io
    /// queries. When set it is used as-is and never disposed by the library; its default
    /// headers are not modified. Takes precedence over <see cref="HttpMessageHandler"/>.
    /// </summary>
    public HttpClient HttpClient { get; set; }

    /// <summary>
    /// A message handler to build the library's <see cref="System.Net.Http.HttpClient"/> from
    /// (useful for proxies and for stubbing in tests). Ignored when <see cref="HttpClient"/> is
    /// set. The handler is not disposed by the library.
    /// </summary>
    public HttpMessageHandler HttpMessageHandler { get; set; }

    /// <summary>
    /// The User-Agent header sent with every HTTP request. <c>null</c> or blank sends
    /// <see cref="DefaultUserAgent"/>. crates.io requires a value that identifies the
    /// application and gives a way to contact its owner.
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// The base address of the Rust release distribution server that hosts the channel and
    /// rustup manifests. Defaults to <c>https://static.rust-lang.org</c>.
    /// </summary>
    public Uri RustDistributionBaseUri { get; set; } = new Uri("https://static.rust-lang.org/");

    /// <summary>
    /// The base address of the crates.io web API. Defaults to
    /// <c>https://crates.io/api/v1/</c>.
    /// </summary>
    public Uri CratesIoApiBaseUri { get; set; } = new Uri("https://crates.io/api/v1/");

    /// <summary>
    /// The minimum time between two crates.io requests made by this process (crates.io asks
    /// API clients for at most one request per second). Defaults to one second; set
    /// <see cref="TimeSpan.Zero"/> to disable throttling, for example against a local mirror.
    /// </summary>
    public TimeSpan CratesIoRequestInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The User-Agent value that will actually be sent: <see cref="UserAgent"/> when it is not
    /// blank, otherwise <see cref="DefaultUserAgent"/>.
    /// </summary>
    public string EffectiveUserAgent => string.IsNullOrWhiteSpace(UserAgent) ? DefaultUserAgent : UserAgent.Trim();
}
