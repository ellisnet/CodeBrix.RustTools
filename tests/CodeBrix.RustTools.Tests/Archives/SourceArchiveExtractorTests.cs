using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Archives;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Archives;

public class SourceArchiveExtractorTests
{
    private const string RootFolderName = "test_project_05-main";

    private static string CreateRootedSampleCopy(SampleSandbox sandbox)
    {
        var rooted = Path.Combine(Path.GetDirectoryName(sandbox.Directory), RootFolderName);
        Directory.Move(sandbox.Directory, rooted);
        return rooted;
    }

    private static string ArchivesFolder(string rootedCopy)
        => Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(rootedCopy), "archives")).FullName;

    private static string OutputFolder(string rootedCopy, string name = "out")
        => Path.Combine(Path.GetDirectoryName(rootedCopy), name);

    private static string CreateZip(string sourceDirectory, string zipPath)
    {
        ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zipPath;
    }

    private static string CreateTar(string sourceDirectory, string tarPath, bool gzip)
    {
        using var file = File.Create(tarPath);
        if (gzip)
        {
            using var compressed = new GZipStream(file, CompressionLevel.Fastest);
            TarFile.CreateFromDirectory(sourceDirectory, compressed, includeBaseDirectory: true);
        }
        else
        {
            TarFile.CreateFromDirectory(sourceDirectory, file, includeBaseDirectory: true);
        }

        return tarPath;
    }

    private static string CreateZipWithEntries(string zipPath, params string[] entryNames)
    {
        using var file = File.Create(zipPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var name in entryNames)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("content of " + name);
        }

        return zipPath;
    }

    private static string CreateTarGzWithEntries(string path, params TarEntry[] entries)
    {
        using var file = File.Create(path);
        using var compressed = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new TarWriter(compressed, TarEntryFormat.Pax);
        foreach (var entry in entries)
        {
            writer.WriteEntry(entry);
        }

        return path;
    }

    private static PaxTarEntry TarFileEntry(string name, string content, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
        => new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            Mode = mode,
        };

    [Fact]
    public void DetectKind_recognises_zip_by_magic_bytes_despite_wrong_extension()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = CreateZipWithEntries(Path.Combine(sandbox.Directory, "archive.bin"), "a.txt");

        //Act
        var kind = new SourceArchiveExtractor().DetectKind(path);

        //Assert
        kind.Should().Be(ArchiveKind.Zip);
    }

    [Fact]
    public void DetectKind_recognises_gzip_and_tar_by_magic_bytes()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var content = sandbox.CreateDirectory("content");
        File.WriteAllText(Path.Combine(content, "file.txt"), "x");
        var gzip = CreateTar(content, Path.Combine(sandbox.Directory, "one.data"), gzip: true);
        var tar = CreateTar(content, Path.Combine(sandbox.Directory, "two.data"), gzip: false);
        var extractor = new SourceArchiveExtractor();

        //Act
        var gzipKind = extractor.DetectKind(gzip);
        var tarKind = extractor.DetectKind(tar);

        //Assert
        gzipKind.Should().Be(ArchiveKind.TarGzip);
        tarKind.Should().Be(ArchiveKind.Tar);
    }

    [Theory]
    [InlineData("source.zip", ArchiveKind.Zip)]
    [InlineData("source.tar", ArchiveKind.Tar)]
    [InlineData("source.tar.gz", ArchiveKind.TarGzip)]
    [InlineData("source.TGZ", ArchiveKind.TarGzip)]
    [InlineData("serde-1.0.0.crate", ArchiveKind.TarGzip)]
    [InlineData("notes.txt", ArchiveKind.Unknown)]
    public void DetectKind_falls_back_to_the_extension(string fileName, ArchiveKind expected)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile(fileName, string.Empty);

        //Act
        var kind = new SourceArchiveExtractor().DetectKind(path);

        //Assert
        kind.Should().Be(expected);
    }

    [Fact]
    public void DetectKind_throws_for_a_missing_file()
    {
        //Act
        var act = () => new SourceArchiveExtractor().DetectKind(Path.Combine(AppContext.BaseDirectory, "no-such-archive.zip"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Extract_zip_strips_the_single_root_folder_and_finds_the_project()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        var rooted = CreateRootedSampleCopy(sandbox);
        var zip = CreateZip(rooted, Path.Combine(ArchivesFolder(rooted), "test_project_05-main.zip"));
        var destination = OutputFolder(rooted);

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(zip, destination);

        //Assert
        extracted.ArchiveKind.Should().Be(ArchiveKind.Zip);
        extracted.RootDirectory.Should().Be(destination);
        extracted.StrippedRootFolderName.Should().Be(RootFolderName);
        extracted.ProjectDirectory.Should().Be(extracted.RootDirectory);
        extracted.ManifestPath.Should().Be(Path.Combine(destination, "Cargo.toml"));
        File.Exists(Path.Combine(destination, "src", "main.rs")).Should().BeTrue();
        extracted.EntryCount.Should().BeGreaterThan(10);
        extracted.TotalBytes.Should().BeGreaterThan(0);
        extracted.DeleteOnDispose.Should().BeFalse();
    }

    [Fact]
    public void Extract_tar_gz_strips_the_single_root_folder_and_finds_the_project()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        var rooted = CreateRootedSampleCopy(sandbox);
        var archive = CreateTar(rooted, Path.Combine(ArchivesFolder(rooted), "test_project_05-main.tar.gz"), gzip: true);

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(archive, OutputFolder(rooted));

        //Assert
        extracted.ArchiveKind.Should().Be(ArchiveKind.TarGzip);
        extracted.StrippedRootFolderName.Should().Be(RootFolderName);
        extracted.ProjectDirectory.Should().Be(extracted.RootDirectory);
        File.Exists(Path.Combine(extracted.RootDirectory, "vendor", "licensed_by_file", "LICENSE.txt")).Should().BeTrue();
    }

    [Fact]
    public void Extract_plain_tar_extracts_the_project()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(6);
        var archive = CreateTar(sandbox.Directory, Path.Combine(Path.GetDirectoryName(sandbox.Directory), "six.tar"), gzip: false);

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(archive, Path.Combine(Path.GetDirectoryName(sandbox.Directory), "out"));

        //Assert
        extracted.ArchiveKind.Should().Be(ArchiveKind.Tar);
        extracted.StrippedRootFolderName.Should().Be("test_project_06");
        File.Exists(Path.Combine(extracted.ProjectDirectory, "build.rs")).Should().BeTrue();
    }

    [Fact]
    public void Extract_without_stripping_keeps_the_root_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        var rooted = CreateRootedSampleCopy(sandbox);
        var zip = CreateZip(rooted, Path.Combine(ArchivesFolder(rooted), "keep.zip"));
        var options = new SourceArchiveOptions { StripSingleRootFolder = false };

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(zip, OutputFolder(rooted), options);

        //Assert
        extracted.StrippedRootFolderName.Should().BeNull();
        extracted.ProjectDirectory.Should().Be(Path.Combine(extracted.RootDirectory, RootFolderName));
    }

    [Fact]
    public void Extract_does_not_strip_when_there_are_several_top_level_entries()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "flat.zip"), "Cargo.toml", "src/main.rs", "README.md");

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        extracted.StrippedRootFolderName.Should().BeNull();
        extracted.ProjectDirectory.Should().Be(extracted.RootDirectory);
        File.Exists(Path.Combine(extracted.RootDirectory, "src", "main.rs")).Should().BeTrue();
    }

    [Fact]
    public void Extract_does_not_strip_two_different_top_level_folders()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "two.zip"), "one/Cargo.toml", "two/Cargo.toml");

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        extracted.StrippedRootFolderName.Should().BeNull();
        extracted.ProjectDirectory.Should().Be(Path.Combine(extracted.RootDirectory, "one"));
    }

    [Fact]
    public void Extract_rejects_a_zip_slip_entry_and_writes_nothing_outside()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "evil.zip"), "root/Cargo.toml", "../evil.txt");
        var destination = Path.Combine(sandbox.Directory, "nested", "out");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, destination);

        //Assert
        act.Should().Throw<SourceArchiveException>().Which.ArchivePath.Should().Be(zip);
        File.Exists(Path.Combine(sandbox.Directory, "nested", "evil.txt")).Should().BeFalse();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void Extract_rejects_a_nested_parent_segment()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "evil.zip"), "root/src/../../../escape.txt");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*'..'*");
    }

    [Fact]
    public void Extract_rejects_an_absolute_zip_entry()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "abs.zip"), "/tmp/codebrix-rusttools-absolute.txt");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*absolute*");
    }

    [Fact]
    public void Extract_rejects_an_absolute_tar_entry()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var archive = CreateTarGzWithEntries(Path.Combine(sandbox.Directory, "abs.tar.gz"), TarFileEntry("/tmp/codebrix-rusttools-absolute.txt", "x"));

        //Act
        var act = () => new SourceArchiveExtractor().Extract(archive, Path.Combine(sandbox.Directory, "out"));

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*absolute*");
    }

    [Fact]
    public void Extract_rejects_a_drive_letter_entry()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "drive.zip"), "C:/Windows/evil.txt");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*drive-letter*");
    }

    [Fact]
    public void Extract_skips_tar_links_and_records_them()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var archive = CreateTarGzWithEntries(
            Path.Combine(sandbox.Directory, "links.tgz"),
            new PaxTarEntry(TarEntryType.Directory, "repo-main/"),
            TarFileEntry("repo-main/Cargo.toml", "[package]\nname = \"x\"\n"),
            new PaxTarEntry(TarEntryType.SymbolicLink, "repo-main/passwd") { LinkName = "/etc/passwd" },
            new PaxTarEntry(TarEntryType.HardLink, "repo-main/hard") { LinkName = "repo-main/Cargo.toml" });

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(archive, Path.Combine(sandbox.Directory, "out"));

        //Assert
        extracted.SkippedEntries.Should().HaveCount(2);
        extracted.SkippedEntries[0].Should().Contain("symbolic link to /etc/passwd");
        File.Exists(Path.Combine(extracted.RootDirectory, "passwd")).Should().BeFalse();
        File.Exists(Path.Combine(extracted.RootDirectory, "hard")).Should().BeFalse();
        extracted.StrippedRootFolderName.Should().Be("repo-main");
    }

    [Fact]
    public void Extract_preserves_the_unix_mode_of_tar_entries()
    {
        //Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
            return;
        }

        using var sandbox = SampleSandbox.CreateEmpty();
        const UnixFileMode executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        var archive = CreateTarGzWithEntries(
            Path.Combine(sandbox.Directory, "modes.tar.gz"),
            TarFileEntry("tool/run.sh", "#!/bin/sh\necho hi\n", executable),
            TarFileEntry("tool/data.txt", "data"));

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(archive, Path.Combine(sandbox.Directory, "out"));

        //Assert
        File.GetUnixFileMode(Path.Combine(extracted.RootDirectory, "run.sh")).Should().Be(executable);
        (File.GetUnixFileMode(Path.Combine(extracted.RootDirectory, "data.txt")) & UnixFileMode.UserExecute).Should().Be(UnixFileMode.None);
    }

    [Fact]
    public void Extract_enforces_the_entry_count_limit()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "many.zip"), "a.txt", "b.txt", "c.txt");
        var options = new SourceArchiveOptions { MaxEntryCount = 2 };

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"), options);

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*MaxEntryCount*");
    }

    [Fact]
    public void Extract_enforces_the_size_limit()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "big.zip"), "a.txt", "b.txt");
        var options = new SourceArchiveOptions { MaxTotalBytes = 10 };

        //Act
        var act = () => new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"), options);

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*MaxTotalBytes*");
    }

    [Fact]
    public void Extract_refuses_to_overwrite_existing_files()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "twice.zip"), "Cargo.toml", "src/main.rs");
        var destination = Path.Combine(sandbox.Directory, "out");
        var extractor = new SourceArchiveExtractor();
        extractor.Extract(zip, destination).Dispose();

        //Act
        var act = () => extractor.Extract(zip, destination);

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*already exists*");
        Directory.Exists(destination).Should().BeTrue();
    }

    [Fact]
    public void Extract_overwrites_existing_files_when_asked()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "twice.zip"), "Cargo.toml", "src/main.rs");
        var destination = Path.Combine(sandbox.Directory, "out");
        var extractor = new SourceArchiveExtractor();
        extractor.Extract(zip, destination).Dispose();
        File.WriteAllText(Path.Combine(destination, "Cargo.toml"), "changed");

        //Act
        using var second = extractor.Extract(zip, destination, new SourceArchiveOptions { Overwrite = true });

        //Assert
        second.EntryCount.Should().Be(2);
        File.ReadAllText(Path.Combine(destination, "Cargo.toml")).Should().Be("content of Cargo.toml");
    }

    [Fact]
    public void Extract_rejects_an_unrecognised_file()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = sandbox.WriteFile("notes.txt", "just some text");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(path);

        //Assert
        act.Should().Throw<SourceArchiveException>().WithMessage("*not a zip*");
    }

    [Fact]
    public void Extract_wraps_a_corrupt_archive_and_cleans_up()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var path = Path.Combine(sandbox.Directory, "corrupt.zip");
        File.WriteAllBytes(path, new byte[] { (byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4, 5, 6, 7, 8 });
        var destination = Path.Combine(sandbox.Directory, "out");

        //Act
        var act = () => new SourceArchiveExtractor().Extract(path, destination);

        //Assert
        act.Should().Throw<SourceArchiveException>().Which.InnerException.Should().NotBeNull();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void Extract_reports_no_project_when_there_is_no_manifest()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "docs.zip"), "docs/readme.md", "docs/guide.md");

        //Act
        using var extracted = new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "out"));

        //Assert
        extracted.ProjectDirectory.Should().BeNull();
        extracted.ManifestPath.Should().BeNull();
    }

    [Fact]
    public void Extract_without_destination_uses_a_temporary_folder_deleted_on_dispose()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "temp.zip"), "crate-1.0/Cargo.toml", "crate-1.0/src/lib.rs");
        var extracted = new SourceArchiveExtractor().Extract(zip);
        var root = extracted.RootDirectory;

        //Act
        var existedBefore = Directory.Exists(root);
        extracted.Dispose();

        //Assert
        existedBefore.Should().BeTrue();
        root.Should().StartWith(SourceArchiveExtractor.DefaultExtractionRoot);
        extracted.DeleteOnDispose.Should().BeTrue();
        extracted.IsDisposed.Should().BeTrue();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Dispose_keeps_a_given_destination_unless_asked_and_is_idempotent()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "keep.zip"), "Cargo.toml");
        var kept = new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "kept"));
        var deleted = new SourceArchiveExtractor().Extract(zip, Path.Combine(sandbox.Directory, "deleted"), new SourceArchiveOptions { DeleteOnDispose = true });

        //Act
        kept.Dispose();
        deleted.Dispose();
        deleted.Dispose();

        //Assert
        Directory.Exists(kept.RootDirectory).Should().BeTrue();
        Directory.Exists(deleted.RootDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteOnDispose_can_be_turned_off_after_extraction()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "temp.zip"), "Cargo.toml");
        var extracted = new SourceArchiveExtractor().Extract(zip);

        //Act
        extracted.DeleteOnDispose = false;
        extracted.Dispose();

        //Assert
        Directory.Exists(extracted.RootDirectory).Should().BeTrue();
        Directory.Delete(extracted.RootDirectory, recursive: true);
    }

    [Fact]
    public async Task ExtractAsync_extracts_on_a_background_thread()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "async.zip"), "pkg/Cargo.toml", "pkg/src/lib.rs");

        //Act
        using var extracted = await new SourceArchiveExtractor().ExtractAsync(zip, Path.Combine(sandbox.Directory, "out"), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        extracted.StrippedRootFolderName.Should().Be("pkg");
        File.Exists(Path.Combine(extracted.RootDirectory, "src", "lib.rs")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_honours_cancellation()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var zip = CreateZipWithEntries(Path.Combine(sandbox.Directory, "cancel.zip"), "pkg/Cargo.toml");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        //Act
        var act = () => new SourceArchiveExtractor().ExtractAsync(zip, Path.Combine(sandbox.Directory, "out"), cancellationToken: cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("src/main.rs", "src/main.rs")]
    [InlineData("./src/./main.rs", "src/main.rs")]
    [InlineData("repo-main/", "repo-main")]
    [InlineData("a\\b\\c.txt", "a/b/c.txt")]
    [InlineData("./", "")]
    public void NormalizeEntryName_returns_relative_forward_slash_paths(string name, string expected)
        => SourceArchiveExtractor.NormalizeEntryName(name, "x.zip").Should().Be(expected);

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\server\\share\\x")]
    [InlineData("C:\\x.txt")]
    [InlineData("")]
    public void NormalizeEntryName_rejects_unsafe_names(string name)
    {
        //Act
        var act = () => SourceArchiveExtractor.NormalizeEntryName(name, "x.zip");

        //Assert
        act.Should().Throw<SourceArchiveException>();
    }

    [Fact]
    public void FindSingleRootFolder_finds_the_shared_folder()
        => SourceArchiveExtractor.FindSingleRootFolder(new[] { ("repo", true), ("repo/Cargo.toml", false), ("repo/src/lib.rs", false) }).Should().Be("repo");

    [Fact]
    public void FindSingleRootFolder_returns_null_for_a_top_level_file()
        => SourceArchiveExtractor.FindSingleRootFolder(new[] { ("repo/Cargo.toml", false), ("README.md", false) }).Should().BeNull();

    [Fact]
    public void FindSingleRootFolder_returns_null_for_a_lone_empty_folder()
        => SourceArchiveExtractor.FindSingleRootFolder(new[] { ("repo", true) }).Should().BeNull();

    [Fact]
    public async Task Extracted_sample_builds_with_cargo()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var rooted = CreateRootedSampleCopy(sandbox);
        var archive = CreateTar(rooted, Path.Combine(ArchivesFolder(rooted), "test_project_05-main.tar.gz"), gzip: true);
        using var extracted = await new SourceArchiveExtractor().ExtractAsync(archive, OutputFolder(rooted), cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var result = await new CargoRunner().BuildAsync(extracted.ProjectDirectory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.RunnableArtifacts.Should().ContainSingle().Which.TargetName.Should().Be("textkit");
        File.Exists(result.RunnableArtifacts[0].Executable).Should().BeTrue();
    }

    private const string PackageManifest = "[package]\nname = \"p\"\nversion = \"0.1.0\"\n";
    private const string WorkspaceManifest = "[workspace]\nmembers = [\"a\"]\n";

    [Fact]
    public void FindProjectManifest_prefers_the_shallowest_manifest()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("deep/er/Cargo.toml", PackageManifest);
        var shallow = sandbox.WriteFile("app/Cargo.toml", PackageManifest);

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().Be(shallow);
    }

    [Fact]
    public void FindProjectManifest_prefers_a_workspace_at_the_same_depth()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("aaa/Cargo.toml", PackageManifest);
        var workspace = sandbox.WriteFile("zzz/Cargo.toml", WorkspaceManifest);

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().Be(workspace);
    }

    [Fact]
    public void FindProjectManifest_prefers_the_workspace_root_over_its_members()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("repo/crates/a/Cargo.toml", PackageManifest);
        var workspace = sandbox.WriteFile("repo/Cargo.toml", "[workspace]\nmembers = [\"crates/a\"]\n");

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().Be(workspace);
    }

    [Fact]
    public void FindProjectManifest_ignores_target_git_and_hidden_folders()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("target/Cargo.toml", PackageManifest);
        sandbox.WriteFile(".git/Cargo.toml", PackageManifest);
        sandbox.WriteFile(".hidden/Cargo.toml", PackageManifest);
        var real = sandbox.WriteFile("src/project/Cargo.toml", PackageManifest);

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().Be(real);
    }

    [Fact]
    public void FindProjectManifest_ignores_a_commented_workspace_table()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("a/Cargo.toml", "# [workspace]\n" + PackageManifest);
        var workspace = sandbox.WriteFile("b/Cargo.toml", "[workspace] # root\nmembers = []\n");

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().Be(workspace);
    }

    [Fact]
    public void FindProjectManifest_returns_null_when_there_is_none()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("docs/readme.md", "# docs");

        //Act
        var manifest = SourceArchiveExtractor.FindProjectManifest(sandbox.Directory);

        //Assert
        manifest.Should().BeNull();
    }

    [Fact]
    public void FindProjectManifest_returns_null_for_a_missing_folder()
        => SourceArchiveExtractor.FindProjectManifest(Path.Combine(Path.GetTempPath(), "codebrix-rusttools-missing-folder")).Should().BeNull();

    [Fact]
    public void FindProjectManifest_finds_the_workspace_root_of_sample_04()
        => SourceArchiveExtractor.FindProjectManifest(RepoPaths.Sample(4)).Should().Be(Path.Combine(RepoPaths.Sample(4), "Cargo.toml"));
}
