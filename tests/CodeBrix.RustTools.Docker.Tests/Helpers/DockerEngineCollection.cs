using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// Groups every test that talks to the real Docker engine, so they share one
/// <see cref="DockerEngineFixture"/> and run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DockerEngineCollection : ICollectionFixture<DockerEngineFixture>
{
    public const string Name = "Docker engine";
}
