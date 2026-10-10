using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The outcome of <c>cargo test</c>: everything in <see cref="CargoCommandResult"/> plus the
/// test counts, summed over every test binary (unit tests, integration tests and doc tests).
/// </summary>
public sealed class CargoTestResult : CargoCommandResult
{
    /// <summary>
    /// The number of tests that passed.
    /// </summary>
    public int TestsPassed { get; init; }

    /// <summary>
    /// The number of tests that failed.
    /// </summary>
    public int TestsFailed { get; init; }

    /// <summary>
    /// The number of tests that were ignored.
    /// </summary>
    public int TestsIgnored { get; init; }

    /// <summary>
    /// The number of benchmarks measured.
    /// </summary>
    public int TestsMeasured { get; init; }

    /// <summary>
    /// The number of tests skipped by the name filter.
    /// </summary>
    public int TestsFilteredOut { get; init; }

    /// <summary>
    /// The number of test binaries that printed a <c>test result:</c> line.
    /// </summary>
    public int TestBinaryCount { get; init; }

    /// <summary>
    /// Every test line libtest printed, in order. Test names can repeat when two test binaries
    /// contain a test with the same path.
    /// </summary>
    public IReadOnlyList<TestOutcome> Tests { get; init; } = Array.Empty<TestOutcome>();

    /// <summary>
    /// The names of the failed tests.
    /// </summary>
    public IReadOnlyList<string> FailedTestNames => Tests.Where(t => t.Failed).Select(t => t.Name).ToList();

    /// <summary>
    /// True when the command succeeded and no test failed.
    /// </summary>
    public bool AllTestsPassed => Success && TestsFailed == 0;

    /// <summary>
    /// Returns a one-line summary with the test counts.
    /// </summary>
    public override string ToString()
        => LicenseGateFailed ? base.ToString() : base.ToString() + $"; tests: {TestsPassed} passed, {TestsFailed} failed, {TestsIgnored} ignored";
}
