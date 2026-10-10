using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The Android cross-compilation image (it downloads the Android NDK when first built), built
/// once for its collection.
/// </summary>
public sealed class AndroidFixture : CrossImageFixture
{
    protected override string Name => "android";

    protected override IReadOnlyList<CrossTarget> Targets => new[] { CrossTarget.AndroidArm64, CrossTarget.AndroidX64 };
}

/// <summary>
/// The tests that build for Android share one <see cref="AndroidFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AndroidCollection : ICollectionFixture<AndroidFixture>
{
    public const string Name = "Docker Android image";
}
