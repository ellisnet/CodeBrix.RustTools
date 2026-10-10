using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Registry;

public class CratesIoClientTests
{
    private static RustWorkbenchOptions StubOptions(StubHttpMessageHandler handler)
        => new RustWorkbenchOptions { HttpMessageHandler = handler, CratesIoRequestInterval = TimeSpan.Zero };

    [Fact]
    public async Task GetCrateAsync_parses_the_crate_and_its_versions()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/itoa", RepoPaths.ReadFixture("crates-io-itoa.json"));
        var client = new CratesIoClient(StubOptions(handler));

        //Act
        var crate = await client.GetCrateAsync("itoa", TestContext.Current.CancellationToken);

        //Assert
        crate.Name.Should().Be("itoa");
        crate.MaxStableVersion.Should().Be("1.0.18");
        crate.NewestVersion.Should().Be("1.0.18");
        crate.LatestVersion.Should().Be("1.0.18");
        crate.Repository.Should().Be("https://github.com/dtolnay/itoa");
        crate.Downloads.Should().BeGreaterThan(0);
        crate.Versions.Should().HaveCount(4);
        crate.Versions[0].Number.Should().Be("1.0.18");
        crate.Versions[0].License.Should().Be("MIT OR Apache-2.0");
        crate.Versions[0].CreatedAt.Should().NotBeNull();
        crate.Versions.Should().OnlyContain(v => !v.IsYanked);
    }

    [Fact]
    public async Task GetCrateAsync_reads_yanked_and_pre_release_versions()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/sample-widget", RepoPaths.ReadFixture("crates-io-yanked-prerelease.json"));
        var client = new CratesIoClient(StubOptions(handler));

        //Act
        var crate = await client.GetCrateAsync("sample-widget", TestContext.Current.CancellationToken);

        //Assert
        crate.LatestVersion.Should().Be("1.4.2");
        crate.NewestVersion.Should().Be("2.0.0-rc.1");
        crate.IsYanked("1.4.3").Should().Be(true);
        crate.IsYanked("1.4.2").Should().Be(false);
        crate.IsYanked("9.9.9").Should().BeNull();
        crate.GetVersion("2.0.0-rc.1").IsPreRelease.Should().BeTrue();
        crate.GetVersion("2.0.0-rc.1").RustVersion.Should().Be("1.85");
        crate.GetVersion("1.0.0").License.Should().BeNull();
        crate.GetVersion("1.4.3").ToString().Should().Be("1.4.3 (yanked)");
    }

    [Fact]
    public async Task GetCrateAsync_sends_the_default_descriptive_user_agent()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/itoa", RepoPaths.ReadFixture("crates-io-itoa.json"));
        var client = new CratesIoClient(StubOptions(handler));

        //Act
        await client.GetCrateAsync("itoa", TestContext.Current.CancellationToken);

        //Assert
        var request = handler.Requests.Single();
        request.UserAgent.Should().Be(RustWorkbenchOptions.DefaultUserAgent);
        request.Uri.ToString().Should().Be("https://crates.io/api/v1/crates/itoa");
    }

    [Fact]
    public async Task GetCrateAsync_sends_a_custom_user_agent()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/itoa", RepoPaths.ReadFixture("crates-io-itoa.json"));
        var options = StubOptions(handler);
        options.UserAgent = "MyBuildServer/2.0 (ops@example.com)";
        var client = new CratesIoClient(options);

        //Act
        await client.GetCrateAsync("itoa", TestContext.Current.CancellationToken);

        //Assert
        handler.Requests.Single().UserAgent.Should().Be("MyBuildServer/2.0 (ops@example.com)");
    }

    [Fact]
    public async Task GetCrateAsync_returns_null_for_an_unknown_crate()
    {
        //Arrange
        var client = new CratesIoClient(StubOptions(new StubHttpMessageHandler()));

        //Act
        var crate = await client.GetCrateAsync("no-such-crate-anywhere", TestContext.Current.CancellationToken);

        //Assert
        crate.Should().BeNull();
    }

    [Fact]
    public async Task GetCrateAsync_throws_for_a_server_error()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/itoa", "{\"errors\":[]}", HttpStatusCode.Forbidden);
        var client = new CratesIoClient(StubOptions(handler));

        //Act
        Func<Task> act = () => client.GetCrateAsync("itoa", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<RustToolsException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../etc/passwd")]
    [InlineData("serde json")]
    public async Task GetCrateAsync_rejects_invalid_crate_names(string name)
    {
        //Arrange
        var client = new CratesIoClient(StubOptions(new StubHttpMessageHandler()));

        //Act
        Func<Task> act = () => client.GetCrateAsync(name, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetLatestVersionAsync_returns_the_max_stable_version()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/sample-widget", RepoPaths.ReadFixture("crates-io-yanked-prerelease.json"));
        var client = new CratesIoClient(StubOptions(handler));

        //Act
        var latest = await client.GetLatestVersionAsync("sample-widget", TestContext.Current.CancellationToken);

        //Assert
        latest.Should().Be("1.4.2");
    }

    [Fact]
    public void ParseCrateResponse_throws_for_invalid_json()
    {
        //Arrange
        Action act = () => CratesIoClient.ParseCrateResponse("<html>not json</html>");

        //Assert
        act.Should().Throw<RustToolsException>();
    }

    [Fact]
    public async Task live_crates_io_lookup_returns_serde()
    {
        //Arrange
        LiveNetwork.SkipUnlessEnabled();
        var client = new CratesIoClient();

        //Act
        var crate = await client.GetCrateAsync("serde", TestContext.Current.CancellationToken);

        //Assert
        crate.Name.Should().Be("serde");
        crate.MaxStableVersion.Should().StartWith("1.");
        crate.Versions.Should().NotBeEmpty();
    }
}
