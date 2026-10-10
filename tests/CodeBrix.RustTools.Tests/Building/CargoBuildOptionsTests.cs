using System;
using System.Linq;
using System.Reflection;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoBuildOptionsTests
{
    [Fact]
    public void license_gate_is_off_and_throws_by_default()
    {
        //Act
        var options = new CargoBuildOptions();

        //Assert
        options.LicensePolicy.Should().BeNull();
        options.ThrowOnLicenseViolation.Should().BeTrue();
        options.LicenseGateMode.Should().Be(LicenseGateMode.Enforce);
        options.ExpectedCrateCount.Should().BeNull();
    }

    [Fact]
    public void Clone_copies_every_setting_and_collection()
    {
        //Arrange
        var policy = LicensePolicy.Permissive();
        var options = new CargoBuildOptions
        {
            Profile = CargoProfile.Release,
            CustomProfileName = "dist",
            AllFeatures = true,
            NoDefaultFeatures = true,
            AllTargets = true,
            TargetTriple = "x86_64-unknown-linux-gnu",
            MacOSInstallName = "@rpath/libx.dylib",
            Locked = true,
            Offline = true,
            Frozen = true,
            Jobs = 3,
            Timeout = TimeSpan.FromMinutes(2),
            WorkingDirectory = "/work",
            Verbose = true,
            KeepRawOutput = true,
            ExpectedCrateCount = 7,
            ThrowOnCancellation = false,
            NoFailFast = true,
            TestNameFilter = "palindrome",
            CheckOnly = false,
            LicensePolicy = policy,
            ThrowOnLicenseViolation = false,
            LicenseGateMode = LicenseGateMode.ReportOnly,
            NoDeps = false,
        };
        options.Features.Add("json");
        options.Packages.Add("core");
        options.Bins.Add("tool");
        options.Examples.Add("demo");
        options.ExtraArguments.Add("--workspace");
        options.Environment["RUST_LOG"] = "debug";

        //Act
        var copy = options.Clone();
        options.Features.Add("later");

        //Assert
        foreach (var property in typeof(CargoBuildOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            property.GetValue(copy).Should().Be(property.GetValue(options), property.Name);
        }

        copy.Features.Should().Equal("json");
        copy.Packages.Should().Equal("core");
        copy.Bins.Should().Equal("tool");
        copy.Examples.Should().Equal("demo");
        copy.ExtraArguments.Should().Equal("--workspace");
        copy.Environment["RUST_LOG"].Should().Be("debug");
        copy.LicensePolicy.Should().BeSameAs(policy);
        copy.LicenseGateMode.Should().Be(LicenseGateMode.ReportOnly);
    }

    [Fact]
    public void Clone_covers_every_public_property()
    {
        //Arrange
        var settable = typeof(CargoBuildOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(p => p.CanWrite);
        var collections = typeof(CargoBuildOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(p => !p.CanWrite);

        //Assert
        settable.Should().Be(24, "a new settable option must be added to CargoBuildOptions.Clone");
        collections.Should().Be(6, "a new collection option must be added to CargoBuildOptions.Clone");
    }
}
