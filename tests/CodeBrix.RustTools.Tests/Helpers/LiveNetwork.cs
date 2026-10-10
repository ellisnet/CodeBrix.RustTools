using System;
using Xunit;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// Gate for tests that reach the real static.rust-lang.org / crates.io services.
/// </summary>
internal static class LiveNetwork
{
    public const string VariableName = "CODEBRIX_RUSTTOOLS_LIVE_TESTS";

    public static void SkipUnlessEnabled()
    {
        if (Environment.GetEnvironmentVariable(VariableName) != "1")
        {
            Assert.Skip($"Live network test; set {VariableName}=1 to run it.");
        }
    }
}
