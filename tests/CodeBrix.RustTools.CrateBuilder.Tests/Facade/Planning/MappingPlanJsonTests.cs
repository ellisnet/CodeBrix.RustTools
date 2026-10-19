using System.Linq;
using System.Text.Json.Nodes;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class MappingPlanJsonTests
{
    [Fact]
    public void Serialize_writes_camel_case_names_string_enums_and_references_by_c_name()
    {
        //Arrange
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Act
        var json = MappingPlanJson.Serialize(plan);
        var root = JsonNode.Parse(json);

        //Assert
        json.Should().EndWith("}\n");
        json.Should().NotContain("\r");
        json.Should().NotContain("null");
        var handle = root["handles"][0];
        handle["cName"].GetValue<string>().Should().Be("datacrate_encoder");
        handle["destroy"].GetValue<string>().Should().Be("datacrate_encoder_destroy");
        handle["methods"].AsArray().Select(m => m.GetValue<string>()).Should().Equal("datacrate_encoder_destroy", "datacrate_encoder_encode", "datacrate_encoder_frames", "datacrate_encoder_new");
        var encode = root["functions"].AsArray().Single(f => f["cName"].GetValue<string>() == "datacrate_encoder_encode");
        encode["kind"].GetValue<string>().Should().Be("mutMethod");
        encode["owner"].GetValue<string>().Should().Be("datacrate_encoder");
        encode["return"]["shape"].GetValue<string>().Should().Be("ownedBuffer");
        encode["parameters"][0]["type"]["handle"].GetValue<string>().Should().Be("datacrate_encoder");
        var config = root["mirrors"].AsArray().Single(m => m["rustPath"].GetValue<string>() == "EncoderConfig");
        config["fields"][0]["type"]["enum"].GetValue<string>().Should().Be("datacrate_mode");
        root["skipped"].AsArray().Select(s => s["rustPath"].GetValue<string>()).Should().Equal("wasm_only");
        root["constants"][0]["managedLiteral"].GetValue<string>().Should().Be("254016000000L");
    }

    [Fact]
    public void Serialize_writes_mirror_references_inside_type_mappings_as_names()
    {
        //Arrange
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Act
        var root = JsonNode.Parse(MappingPlanJson.Serialize(plan));

        //Assert
        var dot = root["functions"].AsArray().Single(f => f["cName"].GetValue<string>() == "datacrate_vec2_dot");
        dot["parameters"][0]["type"]["mirror"].GetValue<string>().Should().Be("datacrate_vec2");
        dot["ownerRustPath"].GetValue<string>().Should().Be("Vec2");
        dot["owner"].Should().BeNull();
    }
}
