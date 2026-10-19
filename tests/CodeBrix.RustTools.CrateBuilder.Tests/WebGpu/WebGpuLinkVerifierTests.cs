using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

public class WebGpuLinkVerifierTests
{
    public static TheoryData<CrateTarget> AllTargets
    {
        get
        {
            var data = new TheoryData<CrateTarget>();
            foreach (var target in CrateTargets.All)
            {
                data.Add(target);
            }

            return data;
        }
    }

    [Fact]
    public void ExpectedLinkName_is_the_soname_install_name_or_dll_name_of_the_engine()
    {
        //Assert
        WebGpuLinkVerifier.ExpectedLinkName(CrateTarget.LinuxX64).Should().Be("libcodebrix_wgpu.so");
        WebGpuLinkVerifier.ExpectedLinkName(CrateTarget.AndroidArm64).Should().Be("libcodebrix_wgpu.so");
        WebGpuLinkVerifier.ExpectedLinkName(CrateTarget.OsxArm64).Should().Be("@rpath/libcodebrix_wgpu.dylib");
        WebGpuLinkVerifier.ExpectedLinkName(CrateTarget.WinX64).Should().Be("codebrix_wgpu.dll");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Verify_rejects_the_engine_itself_because_it_links_no_codebrix_wgpu_and_exports_the_webgpu_api(CrateTarget target)
    {
        //Arrange
        var location = WebGpuTestPackage.LocateOrSkip();
        using var sandbox = SampleSandbox.CreateEmpty();
        var copy = Path.Combine(sandbox.Directory, NativePackageLocator.NativeFileName(target));
        File.Copy(location.NativeLibraryPath(target), copy);

        //Act
        var notes = new WebGpuLinkVerifier().Verify(copy, target, null, out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().Contain(n => n.StartsWith("WebGPU link FAILED") && n.Contains("does not link " + WebGpuLinkVerifier.ExpectedLinkName(target)));
        notes.Should().Contain(n => n.StartsWith("WebGPU link FAILED") && n.Contains("bundles a WebGPU engine"));
        notes.First().Should().Contain(" links ");
    }

    [Fact]
    public void Verify_rejects_a_file_of_another_operating_systems_format()
    {
        //Arrange
        var location = WebGpuTestPackage.LocateOrSkip();
        using var sandbox = SampleSandbox.CreateEmpty();
        var copy = Path.Combine(sandbox.Directory, "codebrix_wgpu.dll");
        File.Copy(location.NativeLibraryPath(CrateTarget.LinuxX64), copy);

        //Act
        var notes = new WebGpuLinkVerifier().Verify(copy, CrateTarget.WinX64, null, out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().Contain(n => n.Contains("is Elf, not the Pe file"));
    }

    [Fact]
    public void Verify_of_an_unreadable_file_fails_with_a_note()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile("libprobe.so", "text\n");

        //Act
        var notes = new WebGpuLinkVerifier().Verify(path, CrateTarget.LinuxX64, null, out var ok);

        //Assert
        ok.Should().BeFalse();
        notes.Should().ContainSingle(n => n.Contains("could not be read"));
    }

    [Fact]
    public void Verify_passes_with_a_warning_when_the_artifact_does_not_reference_the_engine_at_all()
    {
        //Arrange
        var library = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "libSystem.Native.so");
        if (!OperatingSystem.IsLinux() || !File.Exists(library))
        {
            Assert.Skip("Needs an ELF library that references no WebGPU engine (the .NET runtime's libSystem.Native.so on a Linux host).");
        }

        using var sandbox = SampleSandbox.CreateEmpty();
        var copy = Path.Combine(sandbox.Directory, "libprobe.so");
        File.Copy(library, copy);

        //Act
        var notes = new WebGpuLinkVerifier().Verify(copy, HostLinuxTarget(), null, out var ok);

        //Assert
        ok.Should().BeTrue(string.Join(" | ", notes));
        notes.Should().ContainSingle(n => n.StartsWith(WebGpuLinkVerifier.VerificationWarningPrefix + "WebGPU link: ") && n.Contains("does not reference the engine"));
        notes.Should().NotContain(n => n.Contains("FAILED"));
    }

    [Fact]
    public async Task the_verification_recorder_copies_warning_notes_into_the_build_warnings()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = new CrateBuilderContext(new CrateBuilderOptions(), sandbox.Directory, null, null, null);
        var target = new CrateTargetResult
        {
            Target = CrateTarget.LinuxX64,
            Rid = "linux-x64",
            Outcome = CrateTargetOutcome.Compiled,
            VerificationNotes = new[] { "an ordinary note", WebGpuLinkVerifier.VerificationWarningPrefix + "WebGPU link: not used." },
        };

        //Act
        await new VerificationRecorder().VerifyAsync(null, new[] { target }, new CrateBuildRequest(), context, TestContext.Current.CancellationToken);

        //Assert
        context.Warnings.Should().Equal("linux-x64: WebGPU link: not used.");
        target.Outcome.Should().Be(CrateTargetOutcome.Compiled);
    }

    private static CrateTarget HostLinuxTarget()
        => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => CrateTarget.LinuxArm64,
            System.Runtime.InteropServices.Architecture.RiscV64 => CrateTarget.LinuxRiscV64,
            _ => CrateTarget.LinuxX64,
        };
}
