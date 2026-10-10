using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

/// <summary>
/// Builds synthetic CargoMetadata graphs, and runs offline cargo metadata on sandboxed samples
/// (skipping when cargo is missing or the local registry cache cannot satisfy the graph).
/// </summary>
internal static class TestMetadata
{
    public const string CratesIo = CargoMetadata.CratesIoSource;

    public static string RegistryId(string name, string version) => $"{CratesIo}#{name}@{version}";

    public static string PathId(string name, string version) => $"path+file:///work/{name}#{version}";

    public static CargoMetadataPackage Registry(string name, string version, string license = "MIT", string licenseFile = null)
        => new CargoMetadataPackage
        {
            Id = RegistryId(name, version),
            Name = name,
            Version = version,
            License = license,
            LicenseFile = licenseFile,
            Source = CratesIo,
            ManifestPath = $"/home/user/.cargo/registry/src/index/{name}-{version}/Cargo.toml",
        };

    public static CargoMetadataPackage Local(string name, string version, string license = null, string licenseFile = null)
        => new CargoMetadataPackage
        {
            Id = PathId(name, version),
            Name = name,
            Version = version,
            License = license,
            LicenseFile = licenseFile,
            ManifestPath = $"/work/{name}/Cargo.toml",
        };

    public static CargoResolveNode Node(CargoMetadataPackage package, params CargoMetadataPackage[] dependencies)
        => Node(package, CargoDependencyKind.Normal, dependencies);

    public static CargoResolveNode Node(CargoMetadataPackage package, CargoDependencyKind kind, params CargoMetadataPackage[] dependencies)
        => new CargoResolveNode
        {
            Id = package.Id,
            Deps = dependencies.Select(d => new CargoResolveDependency
            {
                Name = d.Name.Replace('-', '_'),
                PackageId = d.Id,
                DepKinds = new[] { new CargoResolveDependencyKind { Kind = kind } },
            }).ToList(),
        };

    public static CargoMetadata Graph(IEnumerable<CargoMetadataPackage> members, IEnumerable<CargoMetadataPackage> others, params CargoResolveNode[] nodes)
    {
        var memberList = members.ToList();
        return new CargoMetadata
        {
            Packages = memberList.Concat(others).ToList(),
            WorkspaceMembers = memberList.Select(m => m.Id).ToList(),
            ResolveNodes = nodes,
            ResolveRoot = memberList.Count == 1 ? memberList[0].Id : null,
            WorkspaceRoot = "/work",
            TargetDirectory = "/work/target",
            FormatVersion = 1,
        };
    }

    /// <summary>Runs offline cargo metadata on a sandboxed copy of a sample, or skips the test.</summary>
    public static async Task<CargoMetadata> OfflineAsync(SampleSandbox sandbox)
    {
        CargoAvailability.SkipIfMissing();
        var project = CargoProject.Open(sandbox.Directory);
        try
        {
            return await project.GetMetadataAsync(new CargoMetadataOptions { Offline = true }, TestContext.Current.CancellationToken);
        }
        catch (CargoCommandException ex)
        {
            Assert.Skip("Offline cargo metadata failed (the local registry cache does not hold this graph): " + ex.Message);
            return null;
        }
    }
}
