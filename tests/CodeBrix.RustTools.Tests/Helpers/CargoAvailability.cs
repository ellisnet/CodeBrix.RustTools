using System;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;
using Xunit;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// Dynamic skips for tests that need the real cargo on this machine.
/// </summary>
internal static class CargoAvailability
{
    private static readonly Lazy<string> Cargo = new Lazy<string>(() => new ExecutableLocator(new RustWorkbenchOptions()).Locate(RustTool.Cargo));

    public static string CargoPath => Cargo.Value;

    public static bool IsAvailable => CargoPath != null;

    public static void SkipIfMissing()
    {
        if (!IsAvailable)
        {
            Assert.Skip("cargo is not installed on this machine (not on PATH or in ~/.cargo/bin).");
        }
    }
}
