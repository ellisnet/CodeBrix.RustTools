using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The Linux cross-compilation image: every concrete Linux target is requested, and the
/// workbench keeps the layers of those that are not the engine's own architecture (two of the
/// three), built once for its collection.
/// </summary>
public sealed class LinuxCrossFixture : CrossImageFixture
{
    protected override string Name => "linux-cross";

    protected override IReadOnlyList<CrossTarget> Targets => new[] { CrossTarget.LinuxX64, CrossTarget.LinuxArm64, CrossTarget.LinuxRiscV64 };
}

/// <summary>
/// The tests that build Linux binaries of another architecture share one <see cref="LinuxCrossFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LinuxCrossCollection : ICollectionFixture<LinuxCrossFixture>
{
    public const string Name = "Docker Linux cross image";
}
