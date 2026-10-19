using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class CfgEvaluatorTests
{
    [Theory]
    [InlineData("target_arch = \"wasm32\"", CrateTarget.LinuxX64, false)]
    [InlineData("target_arch = \"x86_64\"", CrateTarget.LinuxX64, true)]
    [InlineData("target_arch = \"aarch64\"", CrateTarget.OsxArm64, true)]
    [InlineData("target_arch = \"riscv64\"", CrateTarget.LinuxRiscV64, true)]
    [InlineData("target_os = \"android\"", CrateTarget.AndroidArm64, true)]
    [InlineData("target_os = \"linux\"", CrateTarget.AndroidArm64, false)]
    [InlineData("windows", CrateTarget.WinX64, true)]
    [InlineData("unix", CrateTarget.WinArm64, false)]
    [InlineData("unix", CrateTarget.OsxX64, true)]
    [InlineData("target_env = \"msvc\"", CrateTarget.WinX64, false)]
    [InlineData("target_env = \"gnu\"", CrateTarget.LinuxArm64, true)]
    [InlineData("target_pointer_width = \"32\"", CrateTarget.LinuxX64, false)]
    [InlineData("test", CrateTarget.LinuxX64, false)]
    [InlineData("not(test)", CrateTarget.LinuxX64, true)]
    [InlineData("all(unix, not(target_os = \"macos\"))", CrateTarget.OsxArm64, false)]
    [InlineData("all(unix, not(target_os = \"macos\"))", CrateTarget.LinuxX64, true)]
    [InlineData("any(windows, target_os = \"macos\")", CrateTarget.LinuxX64, false)]
    [InlineData("any(windows, target_os = \"macos\")", CrateTarget.OsxX64, true)]
    public void Evaluate_decides_target_predicates(string predicate, CrateTarget target, bool expected)
        => CfgEvaluator.Evaluate(predicate, target, new string[0]).Should().Be(expected);

    [Theory]
    [InlineData("feature = \"fast\"")]
    [InlineData("debug_assertions")]
    [InlineData("my_custom_cfg")]
    [InlineData("all(unix, feature = \"fast\")")]
    [InlineData("not(feature = \"fast\")")]
    [InlineData("this is not a predicate")]
    public void Evaluate_leaves_what_it_cannot_decide_unknown(string predicate)
        => CfgEvaluator.Evaluate(predicate, CrateTarget.LinuxX64, new string[0]).Should().BeNull();

    [Theory]
    [InlineData("feature = \"writer\"", false)]
    [InlineData("feature = \"threads\"", true)]
    [InlineData("any(test, feature = \"writer\")", false)]
    [InlineData("not(feature = \"writer\")", true)]
    [InlineData("feature = \"not_declared\"", null)]
    public void Evaluate_turns_a_known_feature_that_is_not_enabled_off(string predicate, bool? expected)
        => CfgEvaluator.Evaluate(predicate, CrateTarget.LinuxX64, new[] { "threads" }, new[] { "threads", "writer" }).Should().Be(expected);

    [Fact]
    public void Evaluate_treats_a_requested_feature_as_enabled()
        => CfgEvaluator.Evaluate("feature = \"extra\"", CrateTarget.LinuxX64, new[] { "extra" }).Should().Be(true);

    [Fact]
    public void EvaluateAll_is_false_when_any_predicate_is_false()
        => CfgEvaluator.EvaluateAll(new[] { "feature = \"x\"", "windows" }, CrateTarget.LinuxX64, new string[0]).Should().Be(false);
}
