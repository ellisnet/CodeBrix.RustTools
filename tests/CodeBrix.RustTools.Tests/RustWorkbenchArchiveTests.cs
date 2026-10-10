using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Archives;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Running;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Tests.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// <c>RustWorkbench.ExtractAndBuildAsync</c> (both overloads) on archives made from sandboxed
/// samples. Archives and extraction folders live next to the sandbox copy, so the sandbox's
/// disposal removes them.
/// </summary>
public class RustWorkbenchArchiveTests
{
    private static string SandboxParent(SampleSandbox sandbox) => Path.GetDirectoryName(sandbox.Directory);

    [Fact]
    public async Task ExtractAndBuildAsync_builds_a_zip_of_sample_05_and_RunAsync_runs_it()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var archive = Path.Combine(SandboxParent(sandbox), "test_project_05-main.zip");
        ZipFile.CreateFromDirectory(sandbox.Directory, archive, CompressionLevel.Fastest, includeBaseDirectory: true);
        var destination = Path.Combine(SandboxParent(sandbox), "extracted");
        var workbench = new RustWorkbench();
        var options = new RunOptions();
        options.Arguments.Add("racecar");

        //Act
        using var result = await workbench.ExtractAndBuildAsync(archive, destination, cancellationToken: TestContext.Current.CancellationToken);
        var run = await workbench.RunAsync(result.Build.RunnableArtifacts.Single(), options, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.Build.StandardError);
        result.Source.StrippedRootFolderName.Should().Be("test_project_05");
        result.Source.ProjectDirectory.Should().Be(destination);
        result.Project.Name.Should().Be("test_project_05");
        run.Succeeded.Should().BeTrue(run.StandardError);
        run.StandardOutput.Should().Contain("palindrome: true");
    }

    [Fact]
    public async Task Dispose_deletes_the_extracted_folder_when_asked()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var archive = Path.Combine(SandboxParent(sandbox), "sample.zip");
        ZipFile.CreateFromDirectory(sandbox.Directory, archive, CompressionLevel.Fastest, includeBaseDirectory: true);
        var destination = Path.Combine(SandboxParent(sandbox), "extracted");
        var result = await new RustWorkbench().ExtractAndBuildAsync(archive, destination, new CargoBuildOptions { Offline = true }, cancellationToken: TestContext.Current.CancellationToken);
        result.Source.DeleteOnDispose = true;

        //Act
        result.Dispose();

        //Assert
        result.Source.IsDisposed.Should().BeTrue();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAndBuildAsync_opens_the_workspace_root_of_a_tar_gz_of_sample_04()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var archive = Path.Combine(SandboxParent(sandbox), "test_project_04-main.tar.gz");
        using (var file = File.Create(archive))
        using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(sandbox.Directory, compressed, includeBaseDirectory: true);
        }

        var destination = Path.Combine(SandboxParent(sandbox), "extracted");
        var policy = LicensePolicy.Permissive();
        policy.ExemptWorkspaceMembers = false;
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy, ThrowOnLicenseViolation = false };

        //Act
        using var result = await new RustWorkbench().ExtractAndBuildAsync(archive, destination, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Source.ProjectDirectory.Should().Be(destination);
        result.Project.IsWorkspaceRoot.Should().BeTrue();
        result.Project.WorkspaceMemberDirectories.Should().HaveCount(3);
        result.Success.Should().BeFalse();
        result.Build.LicenseGateFailed.Should().BeTrue();
        result.Build.LicenseAudit.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
    }

    [Fact]
    public async Task ExtractAndBuildAsync_throws_SourceArchiveException_when_there_is_no_manifest()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("docs/readme.md", "# no Rust here");
        var archive = Path.Combine(sandbox.Directory, "docs.zip");
        ZipFile.CreateFromDirectory(Path.Combine(sandbox.Directory, "docs"), archive, CompressionLevel.Fastest, includeBaseDirectory: true);
        var destination = Path.Combine(sandbox.Directory, "extracted");

        //Act
        var act = () => new RustWorkbench().ExtractAndBuildAsync(archive, destination, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<SourceArchiveException>()).Which;
        exception.ArchivePath.Should().Be(archive);
        exception.Message.Should().Contain("Cargo.toml");
    }

    [Fact]
    public async Task ExtractAndBuildAsync_with_archive_options_enforces_the_entry_limit()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        var archive = Path.Combine(SandboxParent(sandbox), "sample.zip");
        ZipFile.CreateFromDirectory(sandbox.Directory, archive, CompressionLevel.Fastest, includeBaseDirectory: true);
        int entryCount;
        using (var zip = ZipFile.OpenRead(archive))
        {
            entryCount = zip.Entries.Count;
        }

        var destination = Path.Combine(SandboxParent(sandbox), "extracted");
        var archiveOptions = new SourceArchiveOptions { MaxEntryCount = entryCount - 1 };

        //Act
        var act = () => new RustWorkbench().ExtractAndBuildAsync(archive, destination, archiveOptions, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<SourceArchiveException>()).Which;
        exception.Message.Should().Contain("MaxEntryCount");
        Directory.Exists(Path.Combine(destination, "src")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAndBuildAsync_with_generous_archive_options_builds_sample_05()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var archive = Path.Combine(SandboxParent(sandbox), "sample.zip");
        ZipFile.CreateFromDirectory(sandbox.Directory, archive, CompressionLevel.Fastest, includeBaseDirectory: true);
        var destination = Path.Combine(SandboxParent(sandbox), "extracted");
        var archiveOptions = new SourceArchiveOptions { MaxEntryCount = 1000, StripSingleRootFolder = false };

        //Act
        using var result = await new RustWorkbench().ExtractAndBuildAsync(archive, destination, archiveOptions, new CargoBuildOptions { Offline = true }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.Build.StandardError);
        result.Source.StrippedRootFolderName.Should().BeNull();
        result.Source.ProjectDirectory.Should().Be(Path.Combine(destination, "test_project_05"));
        result.Build.RunnableArtifacts.Should().ContainSingle();
    }
}
