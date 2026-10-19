using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

/// <summary>
/// Builds <see cref="CrateBuilderContext"/> instances for the source and staging tests: a run
/// folder inside a test sandbox, a host workbench on request, progress collected in a list.
/// </summary>
internal static class StagingContexts
{
    public static CrateBuilderContext Create(string runDirectory, List<CrateBuildProgress> reports = null, RustWorkbench workbench = null)
    {
        Directory.CreateDirectory(runDirectory);
        return new CrateBuilderContext(new CrateBuilderOptions(), runDirectory, () => workbench, null, p => reports?.Add(p));
    }
}
