using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The Windows cross-compilation image (llvm-mingw for the engine's own architecture and both
/// windows-gnullvm rustup targets), built once for its collection.
/// </summary>
public sealed class WindowsFixture : CrossImageFixture
{
    protected override string Name => "windows";

    protected override IReadOnlyList<CrossTarget> Targets => new[] { CrossTarget.WindowsX64Gnullvm, CrossTarget.WindowsArm64Gnullvm };
}

/// <summary>
/// The tests that build for Windows share one <see cref="WindowsFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WindowsCollection : ICollectionFixture<WindowsFixture>
{
    public const string Name = "Docker Windows image";
}
