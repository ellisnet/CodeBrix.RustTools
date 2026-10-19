using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class FeatureResolverTests
{
    private const string Manifest = """
        [package]
        name = "shape_features"
        version = "0.1.0"
        edition = "2021"

        [features]
        default = ["threads"]
        threads = ["dep:shape_pool", "logging"]
        logging = []
        writer = []
        codecs = ["shape_codec/fast"]
        lazy = ["shape_lazy?/extra"]

        [dependencies]
        shape_pool = { version = "1", optional = true }
        shape_codec = { version = "1", optional = true }
        shape_lazy = { version = "1", optional = true }
        """;

    private static FeatureResolver.Resolution Resolve(bool noDefault = false, bool all = false, params string[] requested)
        => FeatureResolver.Resolve(CargoManifest.Parse(Manifest), requested, noDefault, all);

    [Fact]
    public void Default_features_are_on_with_everything_they_enable()
    {
        //Act
        var resolution = Resolve();

        //Assert
        resolution.Enabled.OrderBy(f => f, System.StringComparer.Ordinal).Should().Equal("default", "logging", "threads");
    }

    [Fact]
    public void Known_features_include_the_implicit_features_of_optional_dependencies_not_named_with_dep()
    {
        //Act
        var resolution = Resolve();

        //Assert
        resolution.Known.Should().Contain(new[] { "default", "threads", "logging", "writer", "codecs", "lazy", "shape_codec", "shape_lazy" });
        resolution.Known.Should().NotContain("shape_pool", "dep:shape_pool hides the implicit feature, as in cargo");
    }

    [Fact]
    public void Without_default_features_only_the_requested_ones_are_on()
    {
        //Act
        var resolution = Resolve(true, false, "writer");

        //Assert
        resolution.Enabled.Should().Equal("writer");
    }

    [Fact]
    public void A_dependency_feature_turns_the_optional_dependency_on_but_a_weak_one_does_not()
    {
        //Act
        var codecs = Resolve(true, false, "codecs");
        var lazy = Resolve(true, false, "lazy");

        //Assert
        codecs.Enabled.Should().Contain("shape_codec");
        lazy.Enabled.Should().NotContain("shape_lazy");
    }

    [Fact]
    public void A_feature_named_with_the_package_prefix_is_the_crates_own()
    {
        //Act
        var resolution = Resolve(true, false, "shape_features/writer");

        //Assert
        resolution.Enabled.Should().Equal("writer");
    }

    [Fact]
    public void All_features_turns_every_known_feature_on()
    {
        //Act
        var resolution = Resolve(false, true);

        //Assert
        resolution.Enabled.Should().Contain(resolution.Known);
    }
}
