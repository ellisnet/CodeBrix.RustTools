using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CodeBrix.RustTools.Dependencies;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Tests.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Dependencies;

public class DependencyInspectorTests
{
    private static readonly CargoMetadataPackage App = TestMetadata.Local("app", "0.1.0");
    private static readonly CargoMetadataPackage RandOld = TestMetadata.Registry("rand", "0.7.3");
    private static readonly CargoMetadataPackage RandNew = TestMetadata.Registry("rand", "0.8.5");
    private static readonly CargoMetadataPackage Libc = TestMetadata.Registry("libc", "0.2.190");
    private static readonly CargoMetadataPackage Helper = TestMetadata.Local("helper", "0.1.0");
    private static readonly CargoMetadataPackage Tester = TestMetadata.Registry("tester", "1.0.0");

    private static CargoMetadata Graph()
        => TestMetadata.Graph(
            new[] { App },
            new[] { RandOld, RandNew, Libc, Helper, Tester },
            new CargoResolveNode
            {
                Id = App.Id,
                Deps = TestMetadata.Node(App, RandNew, Helper).Deps.Concat(TestMetadata.Node(App, CargoDependencyKind.Dev, Tester).Deps).ToList(),
            },
            TestMetadata.Node(Helper, RandOld, Libc),
            TestMetadata.Node(RandNew, Libc),
            TestMetadata.Node(RandOld, Libc),
            TestMetadata.Node(Libc),
            TestMetadata.Node(Tester));

    private static string CrateJson(string name, string maxStable, params (string Number, bool Yanked)[] versions)
        => "{\"crate\":{\"name\":\"" + name + "\",\"max_stable_version\":\"" + maxStable + "\",\"max_version\":\"" + maxStable + "\",\"newest_version\":\"" + maxStable + "\"},"
            + "\"versions\":[" + string.Join(",", versions.Select(v => "{\"num\":\"" + v.Number + "\",\"yanked\":" + (v.Yanked ? "true" : "false") + "}")) + "]}";

    [Fact]
    public void Packages_lists_every_package_sorted()
        => new DependencyInspector(Graph()).Packages.Select(p => p.Name + " " + p.Version)
            .Should().ContainInOrder("app 0.1.0", "helper 0.1.0", "libc 0.2.190", "rand 0.7.3", "rand 0.8.5", "tester 1.0.0");

    [Fact]
    public void FindDuplicateVersions_reports_crates_with_several_versions()
    {
        //Act
        var duplicates = new DependencyInspector(Graph()).FindDuplicateVersions();

        //Assert
        var rand = duplicates.Should().ContainSingle().Which;
        rand.Name.Should().Be("rand");
        rand.Versions.Should().ContainInOrder("0.7.3", "0.8.5");
        rand.ToString().Should().Be("rand: 0.7.3, 0.8.5");
    }

    [Fact]
    public void GetDependencyTree_draws_the_graph_and_marks_repeats()
    {
        //Act
        var lines = new DependencyInspector(Graph()).GetDependencyTree();

        //Assert
        lines.Should().ContainInOrder(
            "app v0.1.0 (/work/app)",
            "├── helper v0.1.0 (/work/helper)",
            "│   ├── libc v0.2.190",
            "│   └── rand v0.7.3",
            "│       └── libc v0.2.190",
            "└── rand v0.8.5",
            "    └── libc v0.2.190");
        lines.Should().HaveCount(7);
        lines.Should().NotContain(l => l.Contains("tester", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDependencyTree_limits_depth_and_can_include_dev_dependencies()
    {
        //Act
        var lines = new DependencyInspector(Graph()).GetDependencyTree(maxDepth: 1, includeDevDependencies: true);

        //Assert
        lines.Should().HaveCount(4);
        lines.Should().Contain("├── rand v0.8.5");
        lines.Should().Contain("└── tester v1.0.0");
    }

    [Fact]
    public void GetDependencyTree_is_safe_for_cycles()
    {
        //Arrange
        var a = TestMetadata.Local("a", "0.1.0");
        var b = TestMetadata.Registry("b", "1.0.0");
        var c = TestMetadata.Registry("c", "1.0.0");
        var metadata = TestMetadata.Graph(new[] { a }, new[] { b, c }, TestMetadata.Node(a, b), TestMetadata.Node(b, c), TestMetadata.Node(c, b));

        //Act
        var lines = new DependencyInspector(metadata).GetDependencyTree();

        //Assert
        lines.Should().ContainInOrder("a v0.1.0 (/work/a)", "└── b v1.0.0", "    └── c v1.0.0", "        └── b v1.0.0 (*)");
        lines.Should().HaveCount(4);
    }

    [Fact]
    public async Task CheckOutdatedAsync_reports_outdated_and_yanked_registry_crates()
    {
        //Arrange
        var handler = new StubHttpMessageHandler()
            .Respond("/api/v1/crates/rand", CrateJson("rand", "0.9.2", ("0.9.2", false), ("0.8.5", false), ("0.7.3", true)))
            .Respond("/api/v1/crates/libc", CrateJson("libc", "0.2.190", ("0.2.190", false)))
            .Respond("/api/v1/crates/tester", CrateJson("tester", "1.0.0", ("1.0.0", true)));
        var client = new CratesIoClient(new RustWorkbenchOptions { HttpMessageHandler = handler, CratesIoRequestInterval = TimeSpan.Zero });

        //Act
        var outdated = await new DependencyInspector(Graph()).CheckOutdatedAsync(client, TestContext.Current.CancellationToken);

        //Assert
        outdated.Select(o => o.ToString()).Should().ContainInOrder(
            "rand 0.7.3 -> 0.9.2 (installed version yanked)",
            "rand 0.8.5 -> 0.9.2",
            "tester 1.0.0 -> 1.0.0 (installed version yanked)");
        outdated.Should().HaveCount(3);
        outdated.Single(o => o.Name == "tester").IsOutdated.Should().BeFalse();
        handler.Requests.Select(r => r.Uri.AbsolutePath).Should().OnlyContain(p => !p.EndsWith("/app", StringComparison.Ordinal) && !p.EndsWith("/helper", StringComparison.Ordinal));
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task CheckOutdatedAsync_skips_crates_unknown_to_crates_io()
    {
        //Arrange
        var client = new CratesIoClient(new RustWorkbenchOptions { HttpMessageHandler = new StubHttpMessageHandler(), CratesIoRequestInterval = TimeSpan.Zero });

        //Act
        var outdated = await new DependencyInspector(Graph()).CheckOutdatedAsync(client, TestContext.Current.CancellationToken);

        //Assert
        outdated.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckOutdatedAsync_surfaces_registry_failures()
    {
        //Arrange
        var handler = new StubHttpMessageHandler().Respond("/api/v1/crates/libc", "{}", HttpStatusCode.InternalServerError);
        var client = new CratesIoClient(new RustWorkbenchOptions { HttpMessageHandler = handler, CratesIoRequestInterval = TimeSpan.Zero });

        //Act
        Func<Task> act = () => new DependencyInspector(Graph()).CheckOutdatedAsync(client, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<RustToolsException>();
    }

    [Fact]
    public async Task GetDependencyTree_lists_a_real_dependency_graph()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(2);
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Act
        var inspector = new DependencyInspector(metadata);
        var tree = inspector.GetDependencyTree(maxDepth: 1);

        //Assert
        tree[0].Should().StartWith("test_project_02 v0.1.0 (");
        tree.Should().Contain(l => l.Contains("clap v4.", StringComparison.Ordinal));
        inspector.Packages.Should().Contain(p => p.Name == "serde_json");
        inspector.FindDuplicateVersions().Should().NotContain(d => d.Versions.Count < 2);
    }

    [Fact]
    public void constructor_rejects_null_metadata()
    {
        //Arrange
        Action act = () => new DependencyInspector(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
