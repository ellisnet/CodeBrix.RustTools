using System;
using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using SilverAssertions;
using Xunit;
using IArtifactVerifier = CodeBrix.RustTools.CrateBuilder.Building.IArtifactVerifier;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building.Verification;

public class DefaultArtifactVerifierTests : IClassFixture<NativeBinariesFixture>
{
    private readonly NativeBinariesFixture _binaries;

    public DefaultArtifactVerifierTests(NativeBinariesFixture binaries)
    {
        _binaries = binaries;
    }

    private string PathOf(string rid)
    {
        if (!_binaries.Available)
        {
            Assert.Skip("CodeBrix.WebGpu.Native " + NativeBinariesFixture.PackageVersion + " is not in the NuGet cache; restore the CrateBuilder project first.");
        }

        return _binaries.PathOf(rid);
    }

    private static StagedWorkspace Cdylib() => new StagedWorkspace { Identity = new CrateSourceIdentity { CrateTypes = new[] { "cdylib" } } };

    [Theory]
    [InlineData("linux-riscv64", CrateTarget.LinuxRiscV64)]
    [InlineData("win-arm64", CrateTarget.WinArm64)]
    [InlineData("osx-x64", CrateTarget.OsxX64)]
    [InlineData("android-arm64", CrateTarget.AndroidArm64)]
    public void Verify_accepts_a_library_of_the_target_architecture(string rid, CrateTarget target)
    {
        //Act
        var notes = new DefaultArtifactVerifier().Verify(PathOf(rid), target, Cdylib(), out var ok);

        //Assert
        ok.Should().BeTrue();
        notes.Should().Contain(n => n.Contains("needs ["));
        notes.Should().Contain(n => n.Contains("exports") && n.Contains("wgpu"));
    }

    [Fact]
    public void Verify_names_the_android_api_level_of_an_android_library_only()
    {
        //Act
        var android = new DefaultArtifactVerifier().Verify(PathOf("android-x64"), CrateTarget.AndroidX64, Cdylib(), out var androidOk);
        var linux = new DefaultArtifactVerifier().Verify(PathOf("linux-x64"), CrateTarget.LinuxX64, Cdylib(), out var linuxOk);

        //Assert
        androidOk.Should().BeTrue();
        linuxOk.Should().BeTrue();
        android.Should().Contain("libcodebrix_wgpu.so: " + DefaultArtifactVerifier.AndroidApiLevelNote + " 33 (it loads on that Android version and later).");
        linux.Should().NotContain(n => n.Contains(DefaultArtifactVerifier.AndroidApiLevelNote));
    }

    [Fact]
    public void Verify_rejects_a_library_of_another_architecture()
    {
        //Act
        var notes = new DefaultArtifactVerifier().Verify(PathOf("linux-arm64"), CrateTarget.LinuxX64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().Contain(n => n.Contains("aarch64") && n.Contains("needs x86_64"));
    }

    [Fact]
    public void Verify_rejects_a_library_of_another_format()
    {
        //Act
        var notes = new DefaultArtifactVerifier().Verify(PathOf("win-x64"), CrateTarget.LinuxX64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().Contain(n => n.Contains("needs Elf"));
    }

    [Fact]
    public void Verify_rejects_a_missing_file()
    {
        //Act
        var notes = new DefaultArtifactVerifier().Verify("/nonexistent/libx.so", CrateTarget.LinuxX64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().ContainSingle();
    }

    [Theory]
    [InlineData(new[] { "cdylib", "rlib" }, false, true)]
    [InlineData(new[] { "rlib" }, false, false)]
    [InlineData(new[] { "rlib" }, true, true)]
    [InlineData(new string[0], false, true)]
    public void RequiresExports_follows_the_crate_types(string[] types, bool facade, bool expected) =>
        DefaultArtifactVerifier.RequiresExports(new StagedWorkspace { IsFacade = facade, Identity = new CrateSourceIdentity { CrateTypes = types } }).Should().Be(expected);

    [Fact]
    public void GlibcFloorVerifier_accepts_a_library_within_a_high_floor()
    {
        //Act
        var notes = new GlibcFloorVerifier("99.0").Verify(PathOf("linux-x64"), CrateTarget.LinuxX64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeTrue();
        notes.Should().ContainSingle(n => n.Contains("within the linux-x64 baseline's 99.0"));
    }

    [Fact]
    public void GlibcFloorVerifier_rejects_a_library_above_the_floor()
    {
        //Act
        var notes = new GlibcFloorVerifier("2.0").Verify(PathOf("linux-x64"), CrateTarget.LinuxX64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().ContainSingle(n => n.Contains("newer than the linux-x64 baseline's 2.0"));
    }

    private sealed class RejectingVerifier : IArtifactVerifier
    {
        public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
        {
            ok = false;
            return new[] { "bundles a second engine" };
        }
    }

    private sealed class ThrowingVerifier : IArtifactVerifier
    {
        public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void ArtifactVerification_fails_a_target_a_verifier_rejects_and_keeps_every_note()
    {
        //Arrange
        var result = new CrateTargetResult { Target = CrateTarget.LinuxX64, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { PathOf("linux-x64") }, VerificationNotes = new[] { "contributed" } };

        //Act
        ArtifactVerification.Apply(result, new IArtifactVerifier[] { new DefaultArtifactVerifier(), new RejectingVerifier() }, Cdylib());

        //Assert
        result.Outcome.Should().Be(CrateTargetOutcome.Failed);
        result.Message.Should().Contain("bundles a second engine");
        result.VerificationNotes[0].Should().Be("contributed");
        result.VerificationNotes.Should().Contain(n => n.Contains("exports"));
        result.ArtifactPaths.Should().HaveCount(1);
    }

    [Fact]
    public void ArtifactVerification_turns_a_verifier_exception_into_a_failure()
    {
        //Arrange
        var result = new CrateTargetResult { Target = CrateTarget.LinuxX64, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { "/nonexistent/libx.so" } };

        //Act
        ArtifactVerification.Apply(result, new IArtifactVerifier[] { new ThrowingVerifier() }, Cdylib());

        //Assert
        result.Outcome.Should().Be(CrateTargetOutcome.Failed);
        result.Message.Should().Contain("boom");
    }

    [Fact]
    public void ArtifactVerification_leaves_a_target_that_did_not_compile_alone()
    {
        //Arrange
        var result = new CrateTargetResult { Target = CrateTarget.LinuxX64, Outcome = CrateTargetOutcome.NotBuildableInThisMode, Message = "no cross gcc" };

        //Act
        ArtifactVerification.Apply(result, new IArtifactVerifier[] { new RejectingVerifier() }, Cdylib());

        //Assert
        result.Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        result.Message.Should().Be("no cross gcc");
    }

    [Fact]
    public async System.Threading.Tasks.Task ArtifactVerificationStage_verifies_every_compiled_target()
    {
        //Arrange
        var good = new CrateTargetResult { Target = CrateTarget.WinX64, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { PathOf("win-x64") } };
        var bad = new CrateTargetResult { Target = CrateTarget.WinArm64, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { PathOf("win-x64") } };

        //Act
        await new ArtifactVerificationStage().VerifyAsync(Cdylib(), new[] { good, bad }, new CrateBuildRequest(), null, TestContext.Current.CancellationToken);

        //Assert
        good.Outcome.Should().Be(CrateTargetOutcome.Compiled);
        bad.Outcome.Should().Be(CrateTargetOutcome.Failed);
    }

    [Theory]
    [InlineData("linux-x64", CrateTarget.LinuxX64, "2.28")]
    [InlineData("linux-arm64", CrateTarget.LinuxArm64, "2.28")]
    [InlineData("linux-riscv64", CrateTarget.LinuxRiscV64, "2.39")]
    public void GlibcFloorVerifier_checks_each_target_against_its_own_manylinux_floor(string rid, CrateTarget target, string floor)
    {
        //Arrange
        var verifier = new GlibcFloorVerifier(ManyLinuxImages.GlibcFloor);

        //Act
        var notes = verifier.Verify(PathOf(rid), target, Cdylib(), out var ok);

        //Assert
        ok.Should().BeTrue(string.Join(" | ", notes));
        verifier.FloorFor(target).Should().Be(System.Version.Parse(floor));
        verifier.Floor.Should().BeNull();
        notes.Should().ContainSingle(n => n.Contains($"within the {rid} baseline's {floor}"));
    }

    [Fact]
    public void GlibcFloorVerifier_rejects_a_riscv64_library_above_a_lower_floor_than_its_own()
    {
        //Act (the riscv64 binary needs glibc 2.34: inside its own 2.39 floor, above the 2.28 of the other two)
        var notes = new GlibcFloorVerifier(_ => "2.28").Verify(PathOf("linux-riscv64"), CrateTarget.LinuxRiscV64, Cdylib(), out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().ContainSingle(n => n.Contains("newer than the linux-riscv64 baseline's 2.28"));
    }
}
