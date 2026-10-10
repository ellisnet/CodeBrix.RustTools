using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// The per-workbench toolchain detection cache of <see cref="RustWorkbench"/>: counted with a
/// synthetic detector, and once end to end with a cargo wrapper that logs every call.
/// </summary>
public class RustWorkbenchToolchainCacheTests
{
    private sealed class CountingDetector
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<RustToolchainInfo> DetectAsync(string projectDirectory, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(TestToolchains.Complete());
        }
    }

    private static (RustWorkbench Workbench, CountingDetector Detector) Create(RustWorkbenchOptions options = null)
    {
        var detector = new CountingDetector();
        return (new RustWorkbench(options, detector.DetectAsync), detector);
    }

    [Fact]
    public async Task DetectToolchainAsync_serves_the_second_call_from_the_cache()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create();

        //Act
        var first = await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        var second = await workbench.DetectToolchainAsync(sandbox.Directory + Path.DirectorySeparatorChar, TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task DetectToolchainAsync_detects_once_for_concurrent_callers()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create();

        //Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken)));

        //Assert
        detector.Count.Should().Be(1);
        results.Should().OnlyContain(r => ReferenceEquals(r, results[0]));
    }

    [Fact]
    public async Task RefreshToolchainAsync_detects_again_and_replaces_the_cached_result()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create();
        var first = await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Act
        var refreshed = await workbench.RefreshToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        var afterwards = await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(2);
        refreshed.Should().NotBeSameAs(first);
        afterwards.Should().BeSameAs(refreshed);
    }

    [Fact]
    public async Task InvalidateToolchainCache_makes_the_next_call_detect_again()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create();
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        await workbench.DetectToolchainAsync(null, TestContext.Current.CancellationToken);

        //Act
        workbench.InvalidateToolchainCache();
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        await workbench.DetectToolchainAsync(null, TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(4);
    }

    [Fact]
    public async Task ToolchainCacheDuration_expiry_makes_the_next_call_detect_again()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create(new RustWorkbenchOptions { ToolchainCacheDuration = TimeSpan.FromMinutes(5) });
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        workbench.Clock = () => now;
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Act
        now = now.AddMinutes(4);
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        var countBeforeExpiry = detector.Count;
        now = now.AddMinutes(2);
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        countBeforeExpiry.Should().Be(1);
        detector.Count.Should().Be(2);
    }

    [Fact]
    public async Task ToolchainCacheDuration_zero_detects_every_time()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var (workbench, detector) = Create(new RustWorkbenchOptions { ToolchainCacheDuration = TimeSpan.Zero });

        //Act
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(2);
    }

    [Fact]
    public void ToolchainCacheDuration_defaults_to_the_workbench_lifetime()
        => new RustWorkbenchOptions().ToolchainCacheDuration.Should().BeNull();

    [Fact]
    public void ToolchainCacheDuration_rejects_a_negative_value()
    {
        //Arrange
        var options = new RustWorkbenchOptions();

        //Act
        var act = () => options.ToolchainCacheDuration = TimeSpan.FromSeconds(-1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DetectToolchainAsync_keeps_folders_with_different_toolchain_files_apart()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("a/rust-toolchain.toml", "[toolchain]\nchannel = \"stable\"\n");
        sandbox.WriteFile("b/rust-toolchain.toml", "[toolchain]\nchannel = \"nightly\"\n");
        var (workbench, detector) = Create();

        //Act
        var a = await workbench.DetectToolchainAsync(Path.Combine(sandbox.Directory, "a"), TestContext.Current.CancellationToken);
        var b = await workbench.DetectToolchainAsync(Path.Combine(sandbox.Directory, "b"), TestContext.Current.CancellationToken);
        var aAgain = await workbench.DetectToolchainAsync(Path.Combine(sandbox.Directory, "a"), TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(2);
        b.Should().NotBeSameAs(a);
        aAgain.Should().BeSameAs(a);
    }

    [Fact]
    public async Task DetectToolchainAsync_detects_again_after_the_toolchain_file_changes()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("rust-toolchain.toml", "[toolchain]\nchannel = \"stable\"\n");
        var (workbench, detector) = Create();
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Act
        sandbox.WriteFile("rust-toolchain.toml", "[toolchain]\nchannel = \"1.90.0\"\n");
        await workbench.DetectToolchainAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        detector.Count.Should().Be(2);
    }

    [Fact]
    public async Task DetectToolchainAsync_does_not_cache_a_missing_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workbench = new RustWorkbench();

        //Act
        var act = () => workbench.DetectToolchainAsync(Path.Combine(sandbox.Directory, "missing"), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public void ToolchainCacheKey_differs_by_folder_and_toolchain_file_content()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var a = sandbox.CreateDirectory("a");
        var b = sandbox.CreateDirectory("b");

        //Act
        var keyA = RustWorkbench.ToolchainCacheKey(a);
        var keyB = RustWorkbench.ToolchainCacheKey(b);
        sandbox.WriteFile("a/rust-toolchain.toml", "[toolchain]\nchannel = \"stable\"\n");
        var keyAWithFile = RustWorkbench.ToolchainCacheKey(a);

        //Assert
        keyA.Should().NotBe(keyB);
        keyAWithFile.Should().NotBe(keyA);
        RustWorkbench.ToolchainCacheKey(null).Should().Be(string.Empty);
        RustWorkbench.ToolchainCacheKey(Path.Combine(a, "missing")).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_twice_through_one_workbench_detects_the_toolchain_once()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The cargo wrapper is a /bin/sh script.");
        }

        using var sandbox = SampleSandbox.FromSample(5);
        var wrapperDirectory = Path.Combine(Path.GetDirectoryName(sandbox.Directory), "wrapper");
        Directory.CreateDirectory(wrapperDirectory);
        var log = Path.Combine(wrapperDirectory, "calls.txt");
        var wrapper = Path.Combine(wrapperDirectory, "cargo");
        File.WriteAllText(wrapper, "#!/bin/sh\necho \"$*\" >> '" + log + "'\nexec '" + CargoAvailability.CargoPath + "' \"$@\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var workbench = new RustWorkbench(new RustWorkbenchOptions { CargoPath = wrapper });
        var project = workbench.OpenProject(sandbox.Directory);
        int VersionProbes() => File.ReadAllLines(log).Count(l => l.Trim() == "--version");

        //Act
        var first = await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);
        var second = await workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);
        var probesAfterBuilds = VersionProbes();
        await workbench.RefreshToolchainAsync(project.Directory, TestContext.Current.CancellationToken);

        //Assert
        first.Success.Should().BeTrue(first.StandardError);
        second.Success.Should().BeTrue(second.StandardError);
        probesAfterBuilds.Should().Be(1);
        VersionProbes().Should().Be(2);
        File.ReadAllLines(log).Count(l => l.StartsWith("build", StringComparison.Ordinal)).Should().Be(2);
    }
}
