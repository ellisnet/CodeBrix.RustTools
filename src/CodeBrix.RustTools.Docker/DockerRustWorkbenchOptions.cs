using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.Docker;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// Settings for a <see cref="DockerRustWorkbench"/>: how to reach the Docker engine, which base
/// image to build from, how the resources the workbench creates are named and labelled, and the
/// defaults that apply when a call does not say otherwise.
/// </summary>
public sealed class DockerRustWorkbenchOptions
{
    /// <summary>The default base image repository: the official Rust image.</summary>
    public const string DefaultBaseImage = "rust";

    /// <summary>The default tag of the base image.</summary>
    public const string DefaultBaseTag = "latest";

    /// <summary>The default prefix of every label the workbench puts on containers, images and
    /// volumes.</summary>
    public const string DefaultLabelPrefix = "codebrix.rusttools.docker";

    /// <summary>The default name of the cargo registry cache volume.</summary>
    public const string DefaultCacheVolumeName = "codebrix-rusttools-cargo-cache";

    /// <summary>The default repository name of the derived build images the workbench
    /// builds.</summary>
    public const string DefaultBuildImageRepository = "codebrix-rusttools-docker";

    /// <summary>The default prefix of generated container names.</summary>
    public const string DefaultContainerNamePrefix = "codebrix-rusttools";

    private string _buildImageRepository;

    /// <summary>
    /// A complete Docker client configuration (endpoint, Docker CLI path, SSH settings, default
    /// timeout). When <c>null</c> (the default) the workbench builds one from
    /// <see cref="Endpoint"/> and <see cref="DefaultTimeouts"/>.
    /// </summary>
    public DockerClientOptions ClientOptions { get; set; }

    /// <summary>
    /// The Docker endpoint, for example <c>unix:///var/run/docker.sock</c>,
    /// <c>npipe://./pipe/docker_engine</c>, <c>tcp://host:2375</c> or <c>ssh://user@host</c>.
    /// When <c>null</c> the endpoint is resolved the usual way (the <c>DOCKER_HOST</c>
    /// environment variable, then the platform default). When set it overrides the endpoint in
    /// <see cref="ClientOptions"/>.
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>The base image repository the build images start from. Default
    /// <c>rust</c>.</summary>
    public string DefaultImage { get; set; } = DefaultBaseImage;

    /// <summary>The tag of the base image. Default <c>latest</c>.</summary>
    public string DefaultTag { get; set; } = DefaultBaseTag;

    /// <summary>
    /// The prefix of every label the workbench writes. Each container, derived image and volume
    /// it creates carries <c>&lt;prefix&gt;=1</c> plus <c>&lt;prefix&gt;.workbench</c> (the
    /// creating workbench's <see cref="DockerRustWorkbench.InstanceId"/>) and
    /// <c>&lt;prefix&gt;.kind</c>, so cleanup can find exactly what the library made. Default
    /// <c>codebrix.rusttools.docker</c>.
    /// </summary>
    public string LabelPrefix { get; set; } = DefaultLabelPrefix;

    /// <summary>
    /// The name of the named volume that holds cargo's registry cache
    /// (<c>/usr/local/cargo/registry</c> in the container). The git cache
    /// (<c>/usr/local/cargo/git</c>) uses the same name with <c>-git</c> appended. The volumes
    /// persist between containers so repeat builds do not download crates again. Default
    /// <c>codebrix-rusttools-cargo-cache</c>.
    /// </summary>
    public string CacheVolumeName { get; set; } = DefaultCacheVolumeName;

    /// <summary>
    /// The repository name given to derived build images; the tag is a hash of the generated
    /// Dockerfile. When it has not been set (or is set back to <c>null</c>) it follows
    /// <see cref="LabelPrefix"/>: the default prefix gives <c>codebrix-rusttools-docker</c>
    /// (<see cref="DefaultBuildImageRepository"/>), any other prefix gives
    /// <see cref="RepositoryFromLabelPrefix"/> of it (for example
    /// <c>codebrix.rusttools.scratch</c> gives <c>codebrix-rusttools-scratch</c>), so two
    /// workbenches with different prefixes never build, reuse or clean up each other's derived
    /// images. A value set explicitly always wins (see
    /// <see cref="IsBuildImageRepositorySet"/>).
    /// </summary>
    public string BuildImageRepository
    {
        get => _buildImageRepository ?? RepositoryFromLabelPrefix(LabelPrefix);
        set => _buildImageRepository = value;
    }

    /// <summary>
    /// Whether <see cref="BuildImageRepository"/> was set explicitly (to anything other than
    /// <c>null</c>). When <c>false</c> the repository is derived from
    /// <see cref="LabelPrefix"/>.
    /// </summary>
    public bool IsBuildImageRepositorySet => _buildImageRepository != null;

    /// <summary>The prefix of generated container and target-volume names. Default
    /// <c>codebrix-rusttools</c>.</summary>
    public string ContainerNamePrefix { get; set; } = DefaultContainerNamePrefix;

    /// <summary>The time limits for Docker operations.</summary>
    public DockerRustTimeouts DefaultTimeouts { get; set; } = new DockerRustTimeouts();

    /// <summary>
    /// Whether build containers are kept (left running) when they are disposed, unless
    /// <c>BuildContainerOptions.KeepOnDispose</c> says otherwise. Default <c>false</c>: a
    /// disposed container is stopped and removed.
    /// </summary>
    public bool KeepContainerByDefault { get; set; }

    /// <summary>
    /// File and folder names that are never copied from a project folder into a build container.
    /// Default: <c>.git</c>, <c>target</c>, <c>node_modules</c> and <c>.DS_Store</c>.
    /// </summary>
    public IList<string> DefaultExcludePatterns { get; set; } = CreateDefaultExcludePatterns();

    /// <summary>
    /// The exclusion list used when <see cref="DefaultExcludePatterns"/> is left at its
    /// default.
    /// </summary>
    public static IReadOnlyList<string> StandardExcludePatterns { get; } =
        Array.AsReadOnly(new[] { ".git", "target", "node_modules", ".DS_Store" });

    /// <summary>
    /// Returns a deep copy of these options (the <see cref="ClientOptions"/> object is copied
    /// too).
    /// </summary>
    public DockerRustWorkbenchOptions Clone() => new DockerRustWorkbenchOptions
    {
        ClientOptions = ClientOptions == null ? null : CopyClientOptions(ClientOptions),
        Endpoint = Endpoint,
        DefaultImage = DefaultImage,
        DefaultTag = DefaultTag,
        LabelPrefix = LabelPrefix,
        CacheVolumeName = CacheVolumeName,
        _buildImageRepository = _buildImageRepository,
        ContainerNamePrefix = ContainerNamePrefix,
        DefaultTimeouts = (DefaultTimeouts ?? new DockerRustTimeouts()).Clone(),
        KeepContainerByDefault = KeepContainerByDefault,
        DefaultExcludePatterns = DefaultExcludePatterns == null ? new List<string>() : DefaultExcludePatterns.ToList(),
    };

    /// <summary>
    /// The Docker client configuration these options describe: a copy of
    /// <see cref="ClientOptions"/> (or a new one), with <see cref="Endpoint"/> applied when it is
    /// set and, when <see cref="ClientOptions"/> is <c>null</c>, the API-call timeout from
    /// <see cref="DefaultTimeouts"/>.
    /// </summary>
    public DockerClientOptions CreateClientOptions()
    {
        DockerClientOptions result;
        if (ClientOptions != null)
        {
            result = CopyClientOptions(ClientOptions);
        }
        else
        {
            result = new DockerClientOptions();
            var timeouts = DefaultTimeouts ?? new DockerRustTimeouts();
            if (timeouts.ApiCall > TimeSpan.Zero)
            {
                result.DefaultTimeout = timeouts.ApiCall;
            }
        }

        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            result.Endpoint = Endpoint.Trim();
        }

        return result;
    }

    /// <summary>
    /// The derived-image repository name that follows a label prefix: the default prefix
    /// (<c>codebrix.rusttools.docker</c>) gives <see cref="DefaultBuildImageRepository"/>
    /// exactly; any other prefix is trimmed and lower-cased, every run of characters other than
    /// <c>a-z</c> and <c>0-9</c> (dots included) becomes one <c>-</c>, and leading and trailing
    /// <c>-</c> are removed, which always gives a valid Docker repository name. For example
    /// <c>codebrix.rusttools.scratch</c> gives <c>codebrix-rusttools-scratch</c>. Returns an
    /// empty string when the prefix is blank or has no letters or digits.
    /// </summary>
    /// <remarks>
    /// The mapping is not one-to-one: prefixes that differ only in case or punctuation (for
    /// example <c>my.builds</c> and <c>my-builds</c>) give the same repository. Give such
    /// workbenches an explicit <see cref="BuildImageRepository"/> each.
    /// </remarks>
    /// <param name="labelPrefix">The label prefix.</param>
    public static string RepositoryFromLabelPrefix(string labelPrefix)
    {
        if (string.IsNullOrWhiteSpace(labelPrefix))
        {
            return string.Empty;
        }

        var prefix = labelPrefix.Trim();
        if (string.Equals(prefix, DefaultLabelPrefix, StringComparison.Ordinal))
        {
            return DefaultBuildImageRepository;
        }

        var builder = new StringBuilder(prefix.Length);
        var pendingSeparator = false;
        foreach (var raw in prefix)
        {
            var c = char.ToLowerInvariant(raw);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingSeparator = false;
                builder.Append(c);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    internal void Validate()
    {
        RequireText(DefaultImage, nameof(DefaultImage));
        RequireText(DefaultTag, nameof(DefaultTag));
        RequireText(LabelPrefix, nameof(LabelPrefix));
        RequireText(CacheVolumeName, nameof(CacheVolumeName));
        if (!IsBuildImageRepositorySet && string.IsNullOrEmpty(BuildImageRepository))
        {
            throw new ArgumentException(
                $"No build image repository can be derived from the label prefix '{LabelPrefix}' (it has no letters or digits); set BuildImageRepository explicitly.",
                nameof(BuildImageRepository));
        }

        RequireText(BuildImageRepository, nameof(BuildImageRepository));
        RequireText(ContainerNamePrefix, nameof(ContainerNamePrefix));
        if (DefaultTimeouts == null)
        {
            throw new ArgumentException("DefaultTimeouts cannot be null.", nameof(DefaultTimeouts));
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} cannot be null or blank.", name);
        }
    }

    private static List<string> CreateDefaultExcludePatterns() => StandardExcludePatterns.ToList();

    private static DockerClientOptions CopyClientOptions(DockerClientOptions source) => new DockerClientOptions
    {
        Endpoint = source.Endpoint,
        DockerCliPath = source.DockerCliPath,
        SshExecutablePath = source.SshExecutablePath,
        SshArguments = source.SshArguments == null ? new List<string>() : source.SshArguments.ToList(),
        DefaultTimeout = source.DefaultTimeout,
    };
}
