using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class RustToolchainDetectorTests
{
    private const string CurrentRustupShow = """
        Default host: x86_64-unknown-linux-gnu
        rustup home:  /home/someone/.rustup

        installed toolchains
        --------------------
        stable-x86_64-unknown-linux-gnu (active, default)
        nightly-x86_64-unknown-linux-gnu

        active toolchain
        ----------------
        name: stable-x86_64-unknown-linux-gnu
        active because: it's the default toolchain
        installed targets:
          wasm32-unknown-unknown
          x86_64-unknown-linux-gnu
        """;

    private const string OlderRustupShow = """
        Default host: x86_64-unknown-linux-gnu
        rustup home:  /home/someone/.rustup

        installed toolchains
        --------------------

        stable-x86_64-unknown-linux-gnu (default)
        nightly-2024-05-01-x86_64-unknown-linux-gnu

        active toolchain
        ----------------

        nightly-2024-05-01-x86_64-unknown-linux-gnu (overridden by '/work/app/rust-toolchain.toml')
        rustc 1.80.0-nightly (ada5e2c7b 2024-04-30)
        """;

    [Fact]
    public async Task DetectAsync_finds_cargo_and_rustc_on_this_machine()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var detector = new RustToolchainDetector();

        //Act
        var info = await detector.DetectAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        info.Cargo.Status.Should().Be(ToolStatus.Installed);
        info.Rustc.Status.Should().Be(ToolStatus.Installed);
        info.Cargo.Version.IsAtLeast(1, 0).Should().BeTrue();
        info.Rustc.Version.IsAtLeast(1, 0).Should().BeTrue();
        info.Rustc.Version.ToolName.Should().Be("rustc");
        info.HasCargoAndRustc.Should().BeTrue();
        info.HostTriple.Should().NotBeNullOrEmpty();
        info.CargoHome.Should().NotBeNullOrEmpty();
        info.Sysroot.Should().NotBeNullOrEmpty();
        Directory.Exists(Path.Combine(info.Sysroot, "lib", "rustlib")).Should().BeTrue(info.Sysroot);
        info.Tools.Count.Should().Be(Enum.GetValues<RustTool>().Length);
    }

    [Fact]
    public async Task DetectAsync_reports_rustup_state_when_rustup_is_present()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        var detector = new RustToolchainDetector();

        //Act
        var info = await detector.DetectAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        if (!info.IsRustupManaged)
        {
            Assert.Skip("Rust on this machine is not managed by rustup.");
        }

        info.ActiveToolchain.Should().NotBeNullOrEmpty();
        info.InstalledToolchains.Should().Contain(info.ActiveToolchain);
        info.InstalledTargets.Should().Contain(info.HostTriple);
        info.InstalledComponents.Should().NotBeEmpty();
        info.Channel.Should().NotBe(RustChannel.Unknown);
    }

    [Fact]
    public async Task DetectAsync_honours_a_rust_toolchain_file_in_the_project_folder()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("rust-toolchain.toml", "[toolchain]\nchannel = \"stable\"\ncomponents = [\"clippy\"]\n");
        var detector = new RustToolchainDetector();

        //Act
        var info = await detector.DetectAsync(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        info.ProjectDirectory.Should().Be(Path.GetFullPath(sandbox.Directory));
        info.ToolchainFile.Should().NotBeNull();
        info.ToolchainFile.Channel.Should().Be("stable");
        info.ToolchainFile.Components.Should().ContainSingle().Which.Should().Be("clippy");
        if (info.IsRustupManaged && info.ActiveToolchainReason != null)
        {
            info.ActiveToolchainReason.Should().Contain("rust-toolchain.toml");
        }
    }

    [Fact]
    public async Task DetectAsync_reports_every_tool_missing_when_nothing_can_be_found()
    {
        //Arrange
        var locator = new ExecutableLocator(new RustWorkbenchOptions(), string.Empty, null, null, isWindows: false);
        var detector = new RustToolchainDetector(new RustWorkbenchOptions(), locator, OperatingSystemKind.DebianLinux);

        //Act
        var info = await detector.DetectAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        info.Tools.Values.Should().OnlyContain(t => t.Status == ToolStatus.Missing);
        info.Cargo.Message.Should().Contain("cargo");
        info.IsRustupManaged.Should().BeFalse();
        info.OperatingSystem.Should().Be(OperatingSystemKind.DebianLinux);
    }

    [Fact]
    public async Task DetectAsync_throws_for_a_missing_project_folder()
    {
        //Arrange
        var detector = new RustToolchainDetector();
        var missing = Path.Combine(Path.GetTempPath(), "no-such-project-" + Guid.NewGuid().ToString("N"));

        //Act
        Func<Task> act = () => detector.DetectAsync(missing, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public void ParseRustupShow_reads_the_current_format()
    {
        //Act
        var show = RustToolchainDetector.ParseRustupShow(CurrentRustupShow);

        //Assert
        show.DefaultHost.Should().Be("x86_64-unknown-linux-gnu");
        show.RustupHome.Should().Be("/home/someone/.rustup");
        show.ActiveToolchain.Should().Be("stable-x86_64-unknown-linux-gnu");
        show.ActiveReason.Should().Be("it's the default toolchain");
        show.InstalledTargets.Should().ContainInOrder("wasm32-unknown-unknown", "x86_64-unknown-linux-gnu");
    }

    [Fact]
    public void ParseRustupShow_reads_the_older_format()
    {
        //Act
        var show = RustToolchainDetector.ParseRustupShow(OlderRustupShow);

        //Assert
        show.ActiveToolchain.Should().Be("nightly-2024-05-01-x86_64-unknown-linux-gnu");
        show.ActiveReason.Should().Be("overridden by '/work/app/rust-toolchain.toml'");
    }

    [Fact]
    public void ParseRustupShow_reads_the_single_toolchain_format_without_headers()
    {
        //Arrange
        const string output = "Default host: aarch64-apple-darwin\nrustup home:  /Users/me/.rustup\n\nstable-aarch64-apple-darwin (default)\nrustc 1.70.0 (90c541806 2023-05-31)\n";

        //Act
        var show = RustToolchainDetector.ParseRustupShow(output);

        //Assert
        show.DefaultHost.Should().Be("aarch64-apple-darwin");
        show.ActiveToolchain.Should().Be("stable-aarch64-apple-darwin");
        show.ActiveReason.Should().Be("default");
    }

    [Fact]
    public void ParseToolchainList_marks_default_and_active_toolchains()
    {
        //Arrange
        const string output = "stable-x86_64-unknown-linux-gnu (active, default)\nnightly-x86_64-unknown-linux-gnu\n1.80.0-x86_64-unknown-linux-gnu (default)\n";

        //Act
        var toolchains = RustToolchainDetector.ParseToolchainList(output);

        //Assert
        toolchains.Select(t => t.Name).Should().ContainInOrder("stable-x86_64-unknown-linux-gnu", "nightly-x86_64-unknown-linux-gnu", "1.80.0-x86_64-unknown-linux-gnu");
        toolchains[0].IsActive.Should().BeTrue();
        toolchains[0].IsDefault.Should().BeTrue();
        toolchains[1].IsDefault.Should().BeFalse();
        toolchains[2].IsDefault.Should().BeTrue();
    }

    [Fact]
    public void ParseRustcVerbose_reads_host_release_and_llvm()
    {
        //Arrange
        const string output = "rustc 1.94.1 (e408947bf 2026-03-25)\nbinary: rustc\ncommit-hash: e408947bfd200af42db322daf0fadfe7e26d3bd1\ncommit-date: 2026-03-25\nhost: x86_64-unknown-linux-gnu\nrelease: 1.94.1\nLLVM version: 21.1.8\n";

        //Act
        var values = RustToolchainDetector.ParseRustcVerbose(output);

        //Assert
        values["host"].Should().Be("x86_64-unknown-linux-gnu");
        values["release"].Should().Be("1.94.1");
        values["LLVM version"].Should().Be("21.1.8");
        values["commit-hash"].Should().StartWith("e408947bf");
    }

    [Fact]
    public void BuildProbeEnvironment_disables_rustup_auto_install_and_passes_overrides()
    {
        //Arrange
        var options = new RustWorkbenchOptions { CargoHome = "/opt/cargo", RustupHome = "/opt/rustup" };
        options.Environment["EXTRA"] = "1";

        //Act
        var environment = RustToolchainDetector.BuildProbeEnvironment(options);

        //Assert
        environment["RUSTUP_AUTO_INSTALL"].Should().Be("0");
        environment["CARGO_HOME"].Should().Be("/opt/cargo");
        environment["RUSTUP_HOME"].Should().Be("/opt/rustup");
        environment["EXTRA"].Should().Be("1");
    }
}
