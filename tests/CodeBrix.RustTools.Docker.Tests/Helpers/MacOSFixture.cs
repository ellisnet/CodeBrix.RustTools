using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The macOS cross-compilation image (both Darwin targets, llvm-tools and the libSystem stub
/// derived at image build time), built once for its collection.
/// </summary>
public sealed class MacOSFixture : CrossImageFixture
{
    protected override string Name => "macos";

    protected override IReadOnlyList<CrossTarget> Targets => new[] { CrossTarget.MacOSArm64, CrossTarget.MacOSX64 };
}

/// <summary>
/// The tests that build for macOS share one <see cref="MacOSFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MacOSCollection : ICollectionFixture<MacOSFixture>
{
    public const string Name = "Docker macOS image";
}
