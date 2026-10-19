using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class ContributionMergerTests
{
    private sealed class FixedContribution : IBuildContribution
    {
        private readonly BuildContribution _value;

        public FixedContribution(BuildContribution value)
        {
            _value = value;
        }

        public List<(CrateTarget Target, BuildMode Mode)> Calls { get; } = new List<(CrateTarget, BuildMode)>();

        public Task<BuildContribution> PrepareAsync(StagedWorkspace workspace, CrateTarget target, BuildMode mode, CrateBuilderContext context, CancellationToken cancellationToken)
        {
            Calls.Add((target, mode));
            return Task.FromResult(_value);
        }
    }

    private static BuildContribution Contribution(string variable, string value, string flag = null, string note = null)
    {
        var contribution = new BuildContribution();
        contribution.Environment[variable] = value;
        if (flag != null)
        {
            contribution.RustFlags.Add(flag);
        }

        if (note != null)
        {
            contribution.Notes.Add(note);
        }

        return contribution;
    }

    [Fact]
    public async Task PrepareAsync_merges_every_contribution_and_replaces_the_workspace_token()
    {
        //Arrange
        var first = new FixedContribution(Contribution("INCLUDE_DIR", "{workspace}/inc", "-L native={workspace}/lib", "staged {workspace}/lib"));
        var second = new FixedContribution(Contribution("OTHER", "x", "--cfg demo"));

        //Act
        var merged = await ContributionMerger.PrepareAsync(new IBuildContribution[] { first, null, second }, new StagedWorkspace(), CrateTarget.LinuxArm64, BuildMode.Docker, null, "/work", TestContext.Current.CancellationToken);

        //Assert
        merged.Environment["INCLUDE_DIR"].Should().Be("/work/inc");
        merged.Environment["OTHER"].Should().Be("x");
        merged.RustFlags.Should().Equal("-L native=/work/lib", "--cfg demo");
        merged.Notes.Should().Equal("staged /work/lib");
        first.Calls.Should().Equal((CrateTarget.LinuxArm64, BuildMode.Docker));
    }

    [Fact]
    public async Task PrepareAsync_with_no_contributions_is_empty()
    {
        //Act
        var merged = await ContributionMerger.PrepareAsync(null, new StagedWorkspace(), CrateTarget.LinuxX64, BuildMode.Host, null, "/stage", TestContext.Current.CancellationToken);

        //Assert
        merged.Environment.Should().BeEmpty();
        merged.RustFlags.Should().BeEmpty();
        merged.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Merge_lets_a_later_value_win_for_the_same_variable()
    {
        //Arrange
        var into = new BuildContribution();

        //Act
        ContributionMerger.Merge(into, Contribution("A", "first"), "/w");
        ContributionMerger.Merge(into, Contribution("A", "{workspace}/second"), "/w");

        //Assert
        into.Environment["A"].Should().Be("/w/second");
    }

    [Fact]
    public void AppendFlags_joins_existing_flags_and_new_ones_with_spaces() =>
        ContributionMerger.AppendFlags("-C target-feature=+crt-static", new[] { "-L native=/x", " " }).Should().Be("-C target-feature=+crt-static -L native=/x");

    [Fact]
    public void AppendFlags_without_existing_flags_is_just_the_new_ones() =>
        ContributionMerger.AppendFlags(null, new[] { "-L native=/x" }).Should().Be("-L native=/x");
}
