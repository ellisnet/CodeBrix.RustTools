using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Accumulates everything a running cargo command writes (JSON messages and plain text on
/// standard output, status lines on standard error), raises progress, and builds the
/// <see cref="CargoCommandResult"/>. The line handlers are thread-safe: standard output and
/// standard error arrive on different threads.
/// </summary>
internal sealed class CargoOutputCollector
{
    private static readonly Regex StatusLine = new Regex(
        @"^\s*(?<verb>Compiling|Checking|Documenting|Running|Doc-tests|Finished|Updating|Locking|Downloading|Downloaded|Adding|Fresh|Blocking|Fixed|Removed|Removing|Cleaning|Packaging|Scraping|Generated|Executable|Summary|Building|Waiting)\s+(?<rest>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CrateAndVersion = new Regex(
        @"^(?<name>\S+) v(?<version>\S+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex BuildScriptWarning = new Regex(
        @"^warning: (?<name>[^\s@:]+)@(?<version>[^\s:]+): (?<message>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CargoDiagnosticLine = new Regex(
        @"^(?<level>warning|error): (?<message>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CargoSummaryLine = new Regex(
        @"^(error: could not compile |(warning|error): build failed, waiting for other jobs|warning: .+ generated \d+ warnings?|error: test failed, to rerun pass|error: \d+ targets? failed:|error: aborting due to|error: doctest failed, to rerun pass)",
        RegexOptions.CultureInvariant);

    private static readonly Regex TestLine = new Regex(
        @"^test (?<name>.+?) \.\.\. (?<result>ok|FAILED|ignored|bench:.*)(, (?<reason>.*))?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex TestResultLine = new Regex(
        @"^test result: (ok|FAILED)\. (?<passed>\d+) passed; (?<failed>\d+) failed; (?<ignored>\d+) ignored; (?<measured>\d+) measured; (?<filtered>\d+) filtered out",
        RegexOptions.CultureInvariant);

    private static readonly Regex FailureHeader = new Regex(
        @"^---- (?<name>.+) (stdout|stderr) ----$",
        RegexOptions.CultureInvariant);

    private static readonly Regex FmtDiffLine = new Regex(
        @"^Diff in (?<file>.+):(?<line>\d+):$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnsiEscape = new Regex(
        @"\x1B(\[[0-9;?]*[ -/]*[@-~]|\([A-Za-z0-9]|[@-Z\\-_])",
        RegexOptions.CultureInvariant);

    private readonly object _lock = new object();
    private readonly CargoCommand _command;
    private readonly bool _keepRaw;
    private readonly int? _expectedCrateCount;
    private readonly Action<CargoProgress> _report;
    private readonly List<CompilerDiagnostic> _jsonDiagnostics = new List<CompilerDiagnostic>();
    private readonly List<PendingText> _stderrDiagnostics = new List<PendingText>();
    private readonly List<PendingText> _fmtDiffs = new List<PendingText>();
    private readonly List<BuildArtifact> _artifacts = new List<BuildArtifact>();
    private readonly List<string> _rawMessages = new List<string>();
    private readonly StringBuilder _plainOutput = new StringBuilder();
    private readonly HashSet<string> _completedPackages = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<TestOutcome> _tests = new List<TestOutcome>();
    private PendingText _openStderrDiagnostic;
    private PendingText _openFmtDiff;
    private TestOutcome _openFailureTarget;
    private StringBuilder _openFailureOutput;
    private int _passed;
    private int _failed;
    private int _ignored;
    private int _measured;
    private int _filteredOut;
    private int _testBinaries;
    private bool _buildFinishedSeen;
    private PendingFinished _pendingFinished;

    public CargoOutputCollector(CargoCommand command, CargoBuildOptions options, Action<CargoProgress> report)
    {
        _command = command;
        _keepRaw = options?.KeepRawOutput ?? false;
        _expectedCrateCount = options?.ExpectedCrateCount;
        _report = report;
    }

    /// <summary>
    /// The number of distinct packages that have produced a compiled (non build-script)
    /// artifact so far.
    /// </summary>
    /// <remarks>
    /// Standard output (the artifact messages) and standard error (the <c>Finished</c> line)
    /// are read on different threads, so cargo's <c>Finished</c> line can arrive before the
    /// last artifact messages. The <see cref="CargoProgressPhase.Finished"/> report is
    /// therefore held until cargo's <c>build-finished</c> message (which follows every
    /// artifact on standard output), the next standard error line, or the end of the run,
    /// whichever comes first, so its <see cref="CargoProgress.Completed"/> is the final count.
    /// </remarks>
    public int Completed
    {
        get
        {
            lock (_lock)
            {
                return _completedPackages.Count;
            }
        }
    }

    public void OnStandardOutputLine(string line)
    {
        if (line == null)
        {
            return;
        }

        CargoProgress progress = null;
        CargoProgress finished = null;
        lock (_lock)
        {
            if (line.Length > 0 && line[0] == '{' && TryHandleJson(line, out progress))
            {
                if (_buildFinishedSeen)
                {
                    finished = TakePendingFinished();
                }
            }
            else
            {
                HandlePlainOutput(line);
            }
        }

        Report(progress);
        Report(finished);
    }

    public void OnStandardErrorLine(string line)
    {
        if (line == null)
        {
            return;
        }

        CargoProgress progress;
        CargoProgress finished;
        lock (_lock)
        {
            finished = TakePendingFinished();
            progress = HandleStandardErrorLine(line);
        }

        Report(finished);
        Report(progress);
    }

    public CargoCommandResult CreateResult(int exitCode, bool timedOut, bool cancelled, TimeSpan duration, string commandLine, string standardError, string workingDirectory)
    {
        CargoProgress finished;
        lock (_lock)
        {
            finished = TakePendingFinished();
        }

        Report(finished);
        lock (_lock)
        {
            CloseFailureSection();
            _openStderrDiagnostic = null;
            _openFmtDiff = null;

            var diagnostics = new List<CompilerDiagnostic>(_jsonDiagnostics);
            diagnostics.AddRange(_stderrDiagnostics.Select(p => p.ToDiagnostic()));
            diagnostics.AddRange(_fmtDiffs.Select(p => p.ToDiagnostic()));

            var meaningful = diagnostics.Where(d => !CargoJsonMessageParser.IsSummary(d)).ToList();
            var warnings = Distinct(meaningful.Where(d => d.IsWarning));
            var errors = Distinct(meaningful.Where(d => d.IsError).OrderBy(d => d.Source == DiagnosticOrigin.Cargo ? 1 : 0));
            var buildScriptWarnings = warnings.Where(d => d.Source == DiagnosticOrigin.BuildScript && d.Code == null && d.Spans.Count == 0).ToList();
            var success = exitCode == 0 && !timedOut && !cancelled;

            if (_command == CargoCommand.Test)
            {
                return new CargoTestResult
                {
                    Command = _command,
                    Success = success,
                    ExitCode = exitCode,
                    Diagnostics = diagnostics,
                    Warnings = warnings,
                    Errors = errors,
                    BuildScriptWarnings = buildScriptWarnings,
                    Artifacts = _artifacts.ToList(),
                    Duration = duration,
                    CommandLine = commandLine ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    StandardError = standardError ?? string.Empty,
                    StandardOutput = _plainOutput.ToString(),
                    RawMessages = _rawMessages.ToList(),
                    TimedOut = timedOut,
                    WasCancelled = cancelled,
                    TestsPassed = _passed,
                    TestsFailed = _failed,
                    TestsIgnored = _ignored,
                    TestsMeasured = _measured,
                    TestsFilteredOut = _filteredOut,
                    TestBinaryCount = _testBinaries,
                    Tests = _tests.ToList(),
                };
            }

            return new CargoCommandResult
            {
                Command = _command,
                Success = success,
                ExitCode = exitCode,
                Diagnostics = diagnostics,
                Warnings = warnings,
                Errors = errors,
                BuildScriptWarnings = buildScriptWarnings,
                Artifacts = _artifacts.ToList(),
                Duration = duration,
                CommandLine = commandLine ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                StandardError = standardError ?? string.Empty,
                StandardOutput = _plainOutput.ToString(),
                RawMessages = _rawMessages.ToList(),
                TimedOut = timedOut,
                WasCancelled = cancelled,
            };
        }
    }

    private static List<CompilerDiagnostic> Distinct(IEnumerable<CompilerDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CompilerDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            var text = string.IsNullOrEmpty(diagnostic.Rendered) ? diagnostic.Message : diagnostic.Rendered;
            if (seen.Add((diagnostic.PackageId ?? diagnostic.PackageName ?? string.Empty) + "\u0001" + text))
            {
                result.Add(diagnostic);
            }
        }

        return result;
    }

    private void Report(CargoProgress progress)
    {
        if (progress != null)
        {
            _report?.Invoke(progress);
        }
    }

    private bool TryHandleJson(string line, out CargoProgress progress)
    {
        progress = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("reason", out var reasonElement)
                || reasonElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (_keepRaw)
            {
                _rawMessages.Add(line);
            }

            switch (reasonElement.GetString())
            {
                case "compiler-message":
                    var diagnostic = CargoJsonMessageParser.ParseCompilerMessage(root);
                    if (diagnostic != null)
                    {
                        _jsonDiagnostics.Add(diagnostic);
                    }

                    break;
                case "compiler-artifact":
                    var artifact = CargoJsonMessageParser.ParseArtifact(root);
                    _artifacts.Add(artifact);
                    if (!artifact.IsBuildScript && _completedPackages.Add(artifact.PackageId))
                    {
                        progress = new CargoProgress
                        {
                            Phase = CargoProgressPhase.CrateCompleted,
                            CrateName = artifact.PackageName,
                            CrateVersion = artifact.PackageVersion,
                            Completed = _completedPackages.Count,
                            Total = _expectedCrateCount,
                            Message = $"{(artifact.Fresh ? "Fresh" : "Built")} {artifact.PackageName} v{artifact.PackageVersion}",
                        };
                    }

                    break;
                case "build-finished":
                    _buildFinishedSeen = true;
                    break;
            }

            return true;
        }
    }

    private void HandlePlainOutput(string line)
    {
        if (_plainOutput.Length > 0)
        {
            _plainOutput.Append('\n');
        }

        _plainOutput.Append(line);

        if (_command == CargoCommand.Fmt)
        {
            HandleFmtLine(StripAnsi(line));
        }
        else if (_command == CargoCommand.Test)
        {
            HandleTestLine(line);
        }
    }

    private void HandleFmtLine(string line)
    {
        var match = FmtDiffLine.Match(line);
        if (match.Success)
        {
            _openFmtDiff = new PendingText(DiagnosticLevel.Warning, "File is not formatted as rustfmt would format it.", DiagnosticOrigin.Cargo)
            {
                Code = "rustfmt",
                FileName = match.Groups["file"].Value,
                Line = CargoJsonMessageParser.ParseCount(match.Groups["line"].Value),
            };
            _openFmtDiff.Text.Append(line);
            _fmtDiffs.Add(_openFmtDiff);
            return;
        }

        _openFmtDiff?.Text.Append('\n').Append(line);
    }

    private void HandleTestLine(string line)
    {
        var result = TestResultLine.Match(line);
        if (result.Success)
        {
            CloseFailureSection();
            _passed += CargoJsonMessageParser.ParseCount(result.Groups["passed"].Value);
            _failed += CargoJsonMessageParser.ParseCount(result.Groups["failed"].Value);
            _ignored += CargoJsonMessageParser.ParseCount(result.Groups["ignored"].Value);
            _measured += CargoJsonMessageParser.ParseCount(result.Groups["measured"].Value);
            _filteredOut += CargoJsonMessageParser.ParseCount(result.Groups["filtered"].Value);
            _testBinaries++;
            return;
        }

        var header = FailureHeader.Match(line);
        if (header.Success)
        {
            CloseFailureSection();
            var name = header.Groups["name"].Value;
            _openFailureTarget = _tests.LastOrDefault(t => t.Failed && t.Name == name && t.Output == null)
                ?? _tests.LastOrDefault(t => t.Failed && t.Name == name);
            _openFailureOutput = new StringBuilder();
            return;
        }

        if (_openFailureOutput != null)
        {
            if (line == "failures:")
            {
                CloseFailureSection();
                return;
            }

            if (_openFailureOutput.Length > 0)
            {
                _openFailureOutput.Append('\n');
            }

            _openFailureOutput.Append(line);
            return;
        }

        var test = TestLine.Match(line);
        if (test.Success)
        {
            var text = test.Groups["result"].Value;
            var status = text switch
            {
                "ok" => TestStatus.Passed,
                "FAILED" => TestStatus.Failed,
                "ignored" => TestStatus.Ignored,
                _ => TestStatus.Measured,
            };
            _tests.Add(new TestOutcome
            {
                Name = test.Groups["name"].Value,
                Status = status,
                IgnoreReason = status == TestStatus.Ignored && test.Groups["reason"].Success ? test.Groups["reason"].Value : null,
            });
        }
    }

    private void CloseFailureSection()
    {
        if (_openFailureOutput != null && _openFailureTarget != null)
        {
            var text = _openFailureOutput.ToString().Trim('\n', '\r');
            _openFailureTarget.Output = _openFailureTarget.Output == null ? text : _openFailureTarget.Output + "\n" + text;
        }

        _openFailureOutput = null;
        _openFailureTarget = null;
    }

    private CargoProgress HandleStandardErrorLine(string rawLine)
    {
        var line = StripAnsi(rawLine);
        var status = StatusLine.Match(line);
        if (status.Success)
        {
            _openStderrDiagnostic = null;
            if (status.Groups["verb"].Value == "Finished" && !_buildFinishedSeen)
            {
                _pendingFinished = new PendingFinished(status.Groups["rest"].Value.Trim(), line.Trim());
                return null;
            }

            return CreateStatusProgress(status.Groups["verb"].Value, status.Groups["rest"].Value.Trim(), line.Trim());
        }

        var buildScript = BuildScriptWarning.Match(line);
        if (buildScript.Success)
        {
            _openStderrDiagnostic = null;
            var pending = new PendingText(DiagnosticLevel.Warning, buildScript.Groups["message"].Value, DiagnosticOrigin.BuildScript)
            {
                PackageName = buildScript.Groups["name"].Value,
            };
            pending.Text.Append(line);
            _stderrDiagnostics.Add(pending);
            return new CargoProgress
            {
                Phase = CargoProgressPhase.Warning,
                CrateName = buildScript.Groups["name"].Value,
                CrateVersion = buildScript.Groups["version"].Value,
                Completed = _completedPackages.Count,
                Total = _expectedCrateCount,
                Message = line.Trim(),
            };
        }

        var diagnosticLine = CargoDiagnosticLine.Match(line);
        if (diagnosticLine.Success)
        {
            var isError = diagnosticLine.Groups["level"].Value == "error";
            _openStderrDiagnostic = null;
            if (!CargoSummaryLine.IsMatch(line))
            {
                _openStderrDiagnostic = new PendingText(isError ? DiagnosticLevel.Error : DiagnosticLevel.Warning, diagnosticLine.Groups["message"].Value, DiagnosticOrigin.Cargo);
                _openStderrDiagnostic.Text.Append(line);
                _stderrDiagnostics.Add(_openStderrDiagnostic);
            }

            return new CargoProgress
            {
                Phase = isError ? CargoProgressPhase.Error : CargoProgressPhase.Warning,
                Completed = _completedPackages.Count,
                Total = _expectedCrateCount,
                Message = line.Trim(),
            };
        }

        _openStderrDiagnostic?.Text.Append('\n').Append(line);
        return null;
    }

    private CargoProgress CreateStatusProgress(string verb, string rest, string message)
    {
        var phase = verb switch
        {
            "Compiling" => CargoProgressPhase.Compiling,
            "Checking" => CargoProgressPhase.Checking,
            "Documenting" => CargoProgressPhase.Documenting,
            "Running" or "Doc-tests" => CargoProgressPhase.Running,
            "Finished" => CargoProgressPhase.Finished,
            "Updating" => CargoProgressPhase.Updating,
            "Locking" => CargoProgressPhase.Locking,
            "Downloading" or "Downloaded" => CargoProgressPhase.Downloading,
            _ => CargoProgressPhase.Other,
        };

        string crateName = null;
        string crateVersion = null;
        if (phase is CargoProgressPhase.Compiling or CargoProgressPhase.Checking or CargoProgressPhase.Documenting or CargoProgressPhase.Downloading)
        {
            var crate = CrateAndVersion.Match(rest);
            if (crate.Success)
            {
                crateName = crate.Groups["name"].Value;
                crateVersion = crate.Groups["version"].Value;
            }
        }
        else if (verb == "Doc-tests")
        {
            crateName = rest;
        }

        return new CargoProgress
        {
            Phase = phase,
            CrateName = crateName,
            CrateVersion = crateVersion,
            Completed = _completedPackages.Count,
            Total = _expectedCrateCount,
            Message = message,
        };
    }

    /// <summary>
    /// Returns the held <c>Finished</c> report, built now so it carries the current count, and
    /// clears it; <c>null</c> when none is held. Called under the lock.
    /// </summary>
    private CargoProgress TakePendingFinished()
    {
        if (_pendingFinished == null)
        {
            return null;
        }

        var pending = _pendingFinished;
        _pendingFinished = null;
        return CreateStatusProgress("Finished", pending.Rest, pending.Message);
    }

    internal static string StripAnsi(string text)
        => text.IndexOf('\x1B') < 0 ? text : AnsiEscape.Replace(text, string.Empty);

    private sealed record PendingFinished(string Rest, string Message);

    private sealed class PendingText
    {
        public PendingText(DiagnosticLevel level, string message, DiagnosticOrigin source)
        {
            Level = level;
            Message = message;
            Source = source;
        }

        public DiagnosticLevel Level { get; }

        public string Message { get; }

        public DiagnosticOrigin Source { get; }

        public string Code { get; init; }

        public string PackageName { get; init; }

        public string FileName { get; init; }

        public int Line { get; init; }

        public StringBuilder Text { get; } = new StringBuilder();

        public CompilerDiagnostic ToDiagnostic() => new CompilerDiagnostic
        {
            Level = Level,
            Code = Code,
            Message = Message,
            Rendered = Text.ToString().TrimEnd('\n', '\r', ' '),
            Spans = FileName == null
                ? Array.Empty<CodeSpan>()
                : new[] { new CodeSpan { FileName = FileName, LineStart = Line, LineEnd = Line, ColumnStart = 1, ColumnEnd = 1, IsPrimary = true } },
            PackageName = PackageName,
            Source = Source,
        };
    }
}
