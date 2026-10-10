using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Registry;

public class RustReleaseClientTests
{
    private static (RustReleaseClient Client, StubHttpMessageHandler Handler) CreateClient()
    {
        var handler = new StubHttpMessageHandler()
            .Respond("/dist/channel-rust-stable.toml", RepoPaths.ReadFixture("channel-rust-stable.toml"))
            .Respond("/rustup/release-stable.toml", RepoPaths.ReadFixture("rustup-release-stable.toml"));
        return (new RustReleaseClient(new RustWorkbenchOptions { HttpMessageHandler = handler }), handler);
    }

    [Fact]
    public async Task GetLatestStableReleaseAsync_parses_the_channel_manifest()
    {
        //Arrange
        var (client, handler) = CreateClient();

        //Act
        var release = await client.GetLatestStableReleaseAsync(TestContext.Current.CancellationToken);

        //Assert
        release.Channel.Should().Be("stable");
        release.Date.Should().Be(new DateOnly(2026, 10, 1));
        release.RustcVersion.ToString().Should().Be("1.99.0");
        release.RustcVersion.CommitHash.Should().Be("b940084d7");
        release.CargoVersion.ToString().Should().Be("1.99.0");
        release.CargoPackageVersion.ToString().Should().Be("0.100.0");
        release.ClippyVersion.ToString().Should().Be("0.1.99");
        release.RustfmtVersion.ToString().Should().Be("1.10.0");
        handler.Requests.Single().Uri.ToString().Should().Be("https://static.rust-lang.org/dist/channel-rust-stable.toml");
    }

    [Fact]
    public async Task GetLatestRustupVersionAsync_parses_the_rustup_manifest()
    {
        //Arrange
        var (client, _) = CreateClient();

        //Act
        var version = await client.GetLatestRustupVersionAsync(TestContext.Current.CancellationToken);

        //Assert
        version.Should().Be(new ToolVersion(1, 29, 1));
    }

    [Fact]
    public async Task GetLatestStableReleaseAsync_throws_on_an_http_error()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/dist/channel-rust-stable.toml", "busy", HttpStatusCode.ServiceUnavailable);
        var client = new RustReleaseClient(new RustWorkbenchOptions { HttpMessageHandler = handler });

        //Act
        Func<Task> act = () => client.GetLatestStableReleaseAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<RustToolsException>();
    }

    [Fact]
    public async Task GetChannelReleaseAsync_requests_the_beta_manifest()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/dist/channel-rust-beta.toml", "date = \"2026-10-05\"\n[pkg.rustc]\nversion = \"1.100.0-beta.4 (0123abcd4 2026-10-04)\"\n");
        var client = new RustReleaseClient(new RustWorkbenchOptions { HttpMessageHandler = handler });

        //Act
        var release = await client.GetChannelReleaseAsync(RustChannel.Beta, TestContext.Current.CancellationToken);

        //Assert
        release.Channel.Should().Be("beta");
        release.RustcVersion.Channel.Should().Be(RustChannel.Beta);
        release.ClippyVersion.Should().BeNull();
    }

    [Fact]
    public void ParseChannelManifest_throws_without_a_rustc_package()
    {
        //Arrange
        Action act = () => RustReleaseClient.ParseChannelManifest("manifest-version = \"2\"\n[pkg.cargo]\nversion = \"0.100.0\"\n");

        //Assert
        act.Should().Throw<RustToolsException>();
    }

    [Fact]
    public async Task live_stable_release_is_at_least_the_installed_minimum()
    {
        //Arrange
        LiveNetwork.SkipUnlessEnabled();
        var client = new RustReleaseClient();

        //Act
        var release = await client.GetLatestStableReleaseAsync(TestContext.Current.CancellationToken);
        var rustup = await client.GetLatestRustupVersionAsync(TestContext.Current.CancellationToken);

        //Assert
        release.RustcVersion.IsAtLeast(1, 94).Should().BeTrue();
        rustup.IsAtLeast(1, 28).Should().BeTrue();
    }
}
