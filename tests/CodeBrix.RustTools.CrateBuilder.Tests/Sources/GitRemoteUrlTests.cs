using CodeBrix.RustTools.CrateBuilder.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Sources;

public class GitRemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo/")]
    [InlineData("https://github.com/owner/repo", "https://GitHub.COM/owner/repo.git/")]
    [InlineData("HTTPS://github.com/owner/repo", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "git@GITHUB.com:owner/repo")]
    [InlineData("file:///tmp/upstream", "file:///tmp/upstream/")]
    public void AreSame_ignores_trailing_git_and_slash_and_host_case(string a, string b) => GitRemoteUrl.AreSame(a, b).Should().BeTrue();

    [Theory]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/Repo")]
    [InlineData("https://github.com/owner/repo", "https://gitlab.com/owner/repo")]
    [InlineData("https://github.com/owner/repo", "https://github.com/other/repo")]
    [InlineData("https://github.com/owner/repo", "https://github.com:8443/owner/repo")]
    public void AreSame_keeps_the_path_case_host_and_port_significant(string a, string b) => GitRemoteUrl.AreSame(a, b).Should().BeFalse();

    [Theory]
    [InlineData("https://github.com/ellisnet/CodeBrix.RustTools", "CodeBrix.RustTools")]
    [InlineData("https://github.com/ellisnet/CodeBrix.RustTools.git/", "CodeBrix.RustTools")]
    [InlineData("git@example.com:owner/my repo.git", "my_repo")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("", "repository")]
    public void RepositoryName_is_the_last_segment_made_safe(string url, string expected) => GitRemoteUrl.RepositoryName(url).Should().Be(expected);
}
