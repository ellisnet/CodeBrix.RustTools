using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Requests;

public class WrapperOptionsTests
{
    [Fact]
    public void Validate_accepts_a_name_and_namespace() => ((Action)(() => new WrapperOptions { AssemblyName = "My.Geometry", RootNamespace = "My.Geometry" }.Validate())).Should().NotThrow();

    [Fact]
    public void Validate_requires_the_assembly_name_and_root_namespace()
    {
        //Act
        var noName = () => new WrapperOptions { RootNamespace = "A" }.Validate();
        var noNamespace = () => new WrapperOptions { AssemblyName = "A" }.Validate();

        //Assert
        noName.Should().Throw<ArgumentException>().WithMessage("*AssemblyName is required*");
        noNamespace.Should().Throw<ArgumentException>().WithMessage("*RootNamespace is required*");
    }

    [Theory]
    [InlineData("My Wrapper")]
    [InlineData("My/Wrapper")]
    [InlineData(".Wrapper")]
    [InlineData("Wrapper.")]
    public void Validate_rejects_an_invalid_assembly_name(string name)
        => ((Action)(() => new WrapperOptions { AssemblyName = name, RootNamespace = "A" }.Validate())).Should().Throw<ArgumentException>().WithMessage("*not a valid assembly name*");

    [Theory]
    [InlineData("1Geometry")]
    [InlineData("My..Geometry")]
    [InlineData("My-Geometry")]
    public void Validate_rejects_an_invalid_root_namespace(string ns)
        => ((Action)(() => new WrapperOptions { AssemblyName = "A", RootNamespace = ns }.Validate())).Should().Throw<ArgumentException>().WithMessage("*not a dotted C# identifier*");

    [Fact]
    public void Validate_checks_the_packaging()
    {
        //Act
        var noId = () => new WrapperOptions { AssemblyName = "A", RootNamespace = "A", Packaging = new WrapperPackaging() }.Validate();
        var badId = () => new WrapperOptions { AssemblyName = "A", RootNamespace = "A", Packaging = new WrapperPackaging { PackageId = "my package" } }.Validate();
        var noLicense = () => new WrapperOptions { AssemblyName = "A", RootNamespace = "A", Packaging = new WrapperPackaging { PackageId = "My.Package", LicenseExpression = "" } }.Validate();
        var fine = () => new WrapperOptions { AssemblyName = "A", RootNamespace = "A", Packaging = new WrapperPackaging { PackageId = "My.Package" } }.Validate();

        //Assert
        noId.Should().Throw<ArgumentException>().WithMessage("*PackageId is required*");
        badId.Should().Throw<ArgumentException>().WithMessage("*not a valid NuGet package id*");
        noLicense.Should().Throw<ArgumentException>().WithMessage("*LicenseExpression*");
        fine.Should().NotThrow();
    }

    [Fact]
    public void defaults_are_a_plain_class_library_with_an_mit_package_license()
    {
        //Act
        var options = new WrapperOptions();
        var packaging = new WrapperPackaging();
        var plan = new WrapPlan();

        //Assert
        options.Packaging.Should().BeNull();
        options.Plan.Should().BeNull();
        options.OutputDirectory.Should().BeNull();
        options.GenerateSmokeTests.Should().BeFalse();
        packaging.LicenseExpression.Should().Be("MIT");
        packaging.Tags.Should().BeEmpty();
        plan.IncludeTypes.Should().BeEmpty();
        plan.Renames.Should().BeEmpty();
        plan.Overrides.Should().BeEmpty();
    }
}
