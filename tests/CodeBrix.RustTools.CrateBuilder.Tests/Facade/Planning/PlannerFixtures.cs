using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

/// <summary>
/// Plans of the checked-in extractor documents (Facade/Model/samples/) and small edits of those
/// models for the planner tests.
/// </summary>
internal static class PlannerFixtures
{
    public static MappingPlan Plan(string sample, WrapPlan wrapPlan = null, PlannerOptions options = null)
        => new MappingPlanner().Plan(ModelSamples.Load(sample), wrapPlan, options);

    public static MappingPlan Plan(CrateApiModel model, WrapPlan wrapPlan = null, PlannerOptions options = null)
        => new MappingPlanner().Plan(model, wrapPlan, options);

    public static ExportedFunction Function(this MappingPlan plan, string rustPath)
        => plan.Functions.SingleOrDefault(f => f.RustPath == rustPath);

    public static SkippedItem SkippedItem(this MappingPlan plan, string rustPath)
        => plan.Skipped.SingleOrDefault(s => s.RustPath == rustPath);

    public static IEnumerable<ParameterRole> Roles(this ExportedFunction f) => f.Parameters.Select(p => p.Role);

    /// <summary>A copy of the free function <paramref name="template"/> under another name
    /// (added to the model's crate root).</summary>
    public static RustFunction AddFreeFunctionCopy(CrateApiModel model, string template, string newName)
    {
        var source = model.FindFunction(template);
        var copy = JsonSerializer.Deserialize<RustFunction>(JsonSerializer.Serialize(source, CrateApiModel.SerializerOptions), CrateApiModel.SerializerOptions);
        copy.Name = newName;
        copy.Id = "fn:crate::" + newName;
        copy.CanonicalPath = "crate::" + newName;
        copy.PublicPaths = new List<string> { "crate::" + newName };
        copy.Signature = source.Signature.Replace(template, newName);
        model.Functions.Add(copy);
        return copy;
    }
}
