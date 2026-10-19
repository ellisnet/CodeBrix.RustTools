using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Detects the Rust toolchain installed on this machine by running the tools themselves
/// (<c>cargo --version</c>, <c>rustc -vV</c>, <c>rustup show</c>, ...). Detection is read-only:
/// it never installs, updates or downloads anything, and it sets <c>RUSTUP_AUTO_INSTALL=0</c>
/// so that a <c>rust-toolchain.toml</c> naming an uninstalled toolchain cannot start a
/// download.
/// </summary>
public sealed class RustToolchainDetector
{
    private readonly RustWorkbenchOptions _options;
    private readonly ExecutableLocator _locator;
    private readonly OperatingSystemKind _operatingSystem;

    /// <summary>
    /// Creates a detector that honours the tool path overrides, environment and probe timeout in
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Library settings, or <c>null</c> for the defaults.</param>
    public RustToolchainDetector(RustWorkbenchOptions options = null)
        : this(options ?? new RustWorkbenchOptions(), null, HostOperatingSystem.Detect())
    {
    }

    internal RustToolchainDetector(RustWorkbenchOptions options, ExecutableLocator locator, OperatingSystemKind operatingSystem)
    {
        _options = options ?? new RustWorkbenchOptions();
        _locator = locator ?? new ExecutableLocator(_options);
        _operatingSystem = operatingSystem;
    }

    /// <summary>
    /// Probes every <see cref="RustTool"/> and, when rustup is present, the active toolchain,
    /// installed toolchains, targets and components.
    /// </summary>
    /// <param name="projectDirectory">A project folder to probe from, so that rustup applies the
    /// folder's <c>rust-toolchain.toml</c> override and the file is reported in
    /// <see cref="RustToolchainInfo.ToolchainFile"/>; <c>null</c> probes from the current
    /// directory without looking for a toolchain file.</param>
    /// <param name="cancellationToken">Cancels detection; running probes are killed.</param>
    /// <returns>The detected environment. Missing tools are reported, not thrown.</returns>
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectDirectory"/> does not
    /// exist.</exception>
    public async Task<RustToolchainInfo> DetectAsync(string projectDirectory = null, CancellationToken cancellationToken = default)
    {
        string workingDirectory = null;
        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            workingDirectory = Path.GetFullPath(projectDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"The project folder '{workingDirectory}' does not exist.");
            }
        }

        var warnings = new ConcurrentQueue<string>();
        var tools = new Dictionary<RustTool, InstalledTool>();

        var rustcTask = ProbeRustcAsync(workingDirectory, warnings, cancellationToken);
        var cargoTask = ProbeVersionAsync(RustTool.Cargo, new[] { "--version" }, workingDirectory, warnings, cancellationToken);
        var rustupTask = ProbeVersionAsync(RustTool.Rustup, new[] { "--version" }, workingDirectory, warnings, cancellationToken);
        var clippyTask = ProbeVersionAsync(RustTool.Clippy, new[] { "--version" }, workingDirectory, warnings, cancellationToken);
        var rustfmtTask = ProbeVersionAsync(RustTool.Rustfmt, new[] { "--version" }, workingDirectory, warnings, cancellationToken);
        var rustdocTask = ProbeVersionAsync(RustTool.Rustdoc, new[] { "--version" }, workingDirectory, warnings, cancellationToken);
        await Task.WhenAll(rustcTask, cargoTask, rustupTask, clippyTask, rustfmtTask, rustdocTask).ConfigureAwait(false);

        var rustc = await rustcTask.ConfigureAwait(false);
        tools[RustTool.Rustc] = rustc.Tool;
        tools[RustTool.Cargo] = await cargoTask.ConfigureAwait(false);
        tools[RustTool.Rustup] = await rustupTask.ConfigureAwait(false);
        tools[RustTool.Clippy] = await clippyTask.ConfigureAwait(false);
        tools[RustTool.Rustfmt] = await rustfmtTask.ConfigureAwait(false);
        tools[RustTool.Rustdoc] = await rustdocTask.ConfigureAwait(false);

        var host = rustc.Values.TryGetValue("host", out var hostTriple) ? hostTriple : null;
        var (compiler, linker) = await ProbeCToolchainAsync(host, workingDirectory, warnings, cancellationToken).ConfigureAwait(false);
        tools[RustTool.CCompiler] = compiler;
        tools[RustTool.Linker] = linker;

        RustupState rustup = null;
        if (tools[RustTool.Rustup].IsAvailable)
        {
            rustup = await ProbeRustupStateAsync(workingDirectory, warnings, cancellationToken).ConfigureAwait(false);
        }

        RustToolchainFile toolchainFile = null;
        if (workingDirectory != null)
        {
            try
            {
                toolchainFile = RustToolchainFile.FindAndLoad(workingDirectory);
            }
            catch (RustToolsException ex)
            {
                warnings.Enqueue(ex.Message);
            }
        }

        AndroidNdk androidNdk = null;
        try
        {
            androidNdk = AndroidNdkLocator.Find(_options.AndroidNdkPath);
            if (androidNdk == null && !string.IsNullOrWhiteSpace(_options.AndroidNdkPath))
            {
                warnings.Enqueue($"Android NDK: '{_options.AndroidNdkPath}' is not an NDK with a toolchain for this machine ({AndroidNdkLocator.HostTag ?? "no supported host tag"}).");
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            warnings.Enqueue("Android NDK: the search failed: " + ex.Message);
        }

        var channel = RustToolchainFile.ChannelOf(rustup?.Show.ActiveToolchain);
        if (channel == RustChannel.Unknown && rustc.Tool.Version != null)
        {
            channel = rustc.Tool.Version.Channel;
        }

        return new RustToolchainInfo
        {
            OperatingSystem = _operatingSystem,
            OperatingSystemDescription = HostOperatingSystem.Describe(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Tools = tools,
            HostTriple = host ?? rustup?.Show.DefaultHost,
            LlvmVersion = rustc.Values.TryGetValue("LLVM version", out var llvm) ? llvm : null,
            Sysroot = rustc.Sysroot,
            AndroidNdk = androidNdk,
            RustcCommitHash = rustc.Values.TryGetValue("commit-hash", out var hash) ? hash : null,
            ActiveToolchain = rustup?.Show.ActiveToolchain,
            ActiveToolchainReason = rustup?.Show.ActiveReason,
            DefaultToolchain = rustup?.Toolchains.FirstOrDefault(t => t.IsDefault)?.Name,
            Channel = channel,
            InstalledToolchains = rustup?.Toolchains.Select(t => t.Name).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            InstalledTargets = rustup?.Targets ?? (IReadOnlyList<string>)Array.Empty<string>(),
            InstalledComponents = rustup?.Components ?? (IReadOnlyList<string>)Array.Empty<string>(),
            CargoHome = _locator.CargoHome,
            RustupHome = FirstNonBlank(_options.RustupHome, System.Environment.GetEnvironmentVariable("RUSTUP_HOME"), rustup?.Show.RustupHome),
            ToolchainFile = toolchainFile,
            ProjectDirectory = workingDirectory,
            DetectedAt = DateTimeOffset.UtcNow,
            Warnings = warnings.ToList(),
        };
    }

    private async Task<InstalledTool> ProbeVersionAsync(RustTool tool, string[] arguments, string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        var path = _locator.Locate(tool);
        if (path == null)
        {
            return InstalledTool.Missing(tool, $"'{_locator.GetExecutableName(tool)}' was not found on PATH or in the cargo bin folder.");
        }

        var (result, error) = await RunProbeAsync(path, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        return ToolFromResult(tool, path, result, error, warnings);
    }

    private InstalledTool ToolFromResult(RustTool tool, string path, ProcessRunResult result, string error, ConcurrentQueue<string> warnings)
    {
        if (result == null)
        {
            warnings.Enqueue($"{tool}: {error}");
            return InstalledTool.Unknown(tool, error, path);
        }

        if (result.TimedOut)
        {
            var message = $"'{result.CommandLine}' did not finish within {_options.ToolProbeTimeout.TotalSeconds:0} seconds.";
            warnings.Enqueue($"{tool}: {message}");
            return InstalledTool.Unknown(tool, message, path);
        }

        if (result.ExitCode != 0)
        {
            var firstError = FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"exit code {result.ExitCode}";
            return LooksNotInstalled(result.StandardError)
                ? new InstalledTool { Tool = tool, Status = ToolStatus.Missing, ExecutablePath = path, Message = firstError }
                : InstalledTool.Unknown(tool, $"'{result.CommandLine}' failed: {firstError}", path);
        }

        var firstLine = FirstLine(result.StandardOutput) ?? FirstLine(result.StandardError);
        if (!ToolVersion.TryParse(firstLine, out var version))
        {
            return InstalledTool.Unknown(tool, $"Unrecognised version output: '{firstLine}'.", path);
        }

        return new InstalledTool
        {
            Tool = tool,
            Status = ToolStatus.Installed,
            ExecutablePath = path,
            Version = version,
            RawVersionOutput = firstLine,
        };
    }

    private async Task<RustcProbe> ProbeRustcAsync(string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        var path = _locator.Locate(RustTool.Rustc);
        if (path == null)
        {
            return new RustcProbe(InstalledTool.Missing(RustTool.Rustc, "'rustc' was not found on PATH or in the cargo bin folder."), new Dictionary<string, string>());
        }

        var (result, error) = await RunProbeAsync(path, new[] { "-vV" }, workingDirectory, cancellationToken).ConfigureAwait(false);
        var tool = ToolFromResult(RustTool.Rustc, path, result, error, warnings);
        var values = result != null && result.ExitCode == 0
            ? ParseRustcVerbose(result.StandardOutput)
            : new Dictionary<string, string>();
        string sysroot = null;
        if (result != null && result.ExitCode == 0)
        {
            var (sysrootResult, sysrootError) = await RunProbeAsync(path, new[] { "--print", "sysroot" }, workingDirectory, cancellationToken).ConfigureAwait(false);
            var line = sysrootResult != null && sysrootResult.ExitCode == 0 ? FirstLine(sysrootResult.StandardOutput) : null;
            if (!string.IsNullOrWhiteSpace(line))
            {
                sysroot = line.Trim();
            }
            else
            {
                warnings.Enqueue("Rustc: 'rustc --print sysroot' gave no usable answer" + (sysrootError == null ? "." : ": " + sysrootError));
            }
        }

        return new RustcProbe(tool, values, sysroot);
    }

    private async Task<(InstalledTool Compiler, InstalledTool Linker)> ProbeCToolchainAsync(string hostTriple, string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        if (_operatingSystem == OperatingSystemKind.Windows)
        {
            if (hostTriple != null && hostTriple.Contains("windows-gnu", StringComparison.OrdinalIgnoreCase))
            {
                return await ProbeUnixStyleCompilerAsync("gcc", workingDirectory, warnings, cancellationToken).ConfigureAwait(false);
            }

            return await ProbeMsvcAsync(workingDirectory, warnings, cancellationToken).ConfigureAwait(false);
        }

        if (_operatingSystem == OperatingSystemKind.MacOS)
        {
            var xcodeSelect = _locator.Locate("xcode-select");
            if (xcodeSelect == null)
            {
                var message = "'xcode-select' was not found, so the Xcode Command Line Tools could not be checked.";
                return (InstalledTool.Unknown(RustTool.CCompiler, message), InstalledTool.Unknown(RustTool.Linker, message));
            }

            var (result, error) = await RunProbeAsync(xcodeSelect, new[] { "-p" }, workingDirectory, cancellationToken).ConfigureAwait(false);
            if (result == null || result.TimedOut)
            {
                var message = error ?? "'xcode-select -p' timed out.";
                return (InstalledTool.Unknown(RustTool.CCompiler, message), InstalledTool.Unknown(RustTool.Linker, message));
            }

            if (result.ExitCode != 0)
            {
                var message = "The Xcode Command Line Tools are not installed ('xcode-select -p' failed).";
                return (InstalledTool.Missing(RustTool.CCompiler, message), InstalledTool.Missing(RustTool.Linker, message));
            }
        }

        return await ProbeUnixStyleCompilerAsync("cc", workingDirectory, warnings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(InstalledTool Compiler, InstalledTool Linker)> ProbeUnixStyleCompilerAsync(string executableName, string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        var path = _locator.Locate(executableName);
        if (path == null)
        {
            var message = $"No C compiler driver ('{executableName}') was found on PATH; rustc needs it to link executables.";
            return (InstalledTool.Missing(RustTool.CCompiler, message), InstalledTool.Missing(RustTool.Linker, message));
        }

        var (result, error) = await RunProbeAsync(path, new[] { "--version" }, workingDirectory, cancellationToken).ConfigureAwait(false);
        var compiler = ToolFromResult(RustTool.CCompiler, path, result, error, warnings);
        var linker = new InstalledTool
        {
            Tool = RustTool.Linker,
            Status = compiler.Status,
            ExecutablePath = compiler.ExecutablePath,
            Version = compiler.Version,
            RawVersionOutput = compiler.RawVersionOutput,
            Message = compiler.Message ?? $"rustc links through the C compiler driver ('{executableName}').",
        };
        return (compiler, linker);
    }

    private async Task<(InstalledTool Compiler, InstalledTool Linker)> ProbeMsvcAsync(string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        var clPath = _locator.Locate("cl");
        var linkPath = _locator.Locate("link");
        if (clPath != null && linkPath != null)
        {
            return (
                new InstalledTool { Tool = RustTool.CCompiler, Status = ToolStatus.Installed, ExecutablePath = clPath, Message = "MSVC cl.exe found on PATH." },
                new InstalledTool { Tool = RustTool.Linker, Status = ToolStatus.Installed, ExecutablePath = linkPath, Message = "MSVC link.exe found on PATH." });
        }

        var programFilesX86 = System.Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (string.IsNullOrEmpty(programFilesX86))
        {
            programFilesX86 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
        }

        var vswhere = string.IsNullOrEmpty(programFilesX86)
            ? null
            : Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (vswhere == null || !File.Exists(vswhere))
        {
            var message = "Neither MSVC (cl.exe/link.exe) on PATH nor the Visual Studio Installer was found; install the Visual Studio Build Tools with the C++ workload.";
            return (InstalledTool.Missing(RustTool.CCompiler, message), InstalledTool.Missing(RustTool.Linker, message));
        }

        var component = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "Microsoft.VisualStudio.Component.VC.Tools.ARM64"
            : "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
        var arguments = new[] { "-latest", "-products", "*", "-requires", component, "-property", "installationPath" };
        var (result, error) = await RunProbeAsync(vswhere, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (result == null || result.TimedOut || result.ExitCode != 0)
        {
            var message = $"The Visual Studio Installer query failed ({error ?? FirstLine(result?.StandardError) ?? "timed out"}); MSVC availability is unknown.";
            warnings.Enqueue(message);
            return (InstalledTool.Unknown(RustTool.CCompiler, message, vswhere), InstalledTool.Unknown(RustTool.Linker, message, vswhere));
        }

        var installationPath = FirstLine(result.StandardOutput);
        if (string.IsNullOrEmpty(installationPath))
        {
            var message = "Visual Studio is installed but without the MSVC C++ build tools (" + component + ").";
            return (InstalledTool.Missing(RustTool.CCompiler, message), InstalledTool.Missing(RustTool.Linker, message));
        }

        const string found = "MSVC C++ build tools found by the Visual Studio Installer; rustc locates them automatically.";
        return (
            new InstalledTool { Tool = RustTool.CCompiler, Status = ToolStatus.Installed, ExecutablePath = installationPath, Message = found },
            new InstalledTool { Tool = RustTool.Linker, Status = ToolStatus.Installed, ExecutablePath = installationPath, Message = found });
    }

    private async Task<RustupState> ProbeRustupStateAsync(string workingDirectory, ConcurrentQueue<string> warnings, CancellationToken cancellationToken)
    {
        var rustup = _locator.Locate(RustTool.Rustup);
        var showTask = RunProbeAsync(rustup, new[] { "show" }, workingDirectory, cancellationToken);
        var toolchainsTask = RunProbeAsync(rustup, new[] { "toolchain", "list" }, workingDirectory, cancellationToken);
        var targetsTask = RunProbeAsync(rustup, new[] { "target", "list", "--installed" }, workingDirectory, cancellationToken);
        var componentsTask = RunProbeAsync(rustup, new[] { "component", "list", "--installed" }, workingDirectory, cancellationToken);
        await Task.WhenAll(showTask, toolchainsTask, targetsTask, componentsTask).ConfigureAwait(false);

        string Output((ProcessRunResult Result, string Error) probe, string what)
        {
            if (probe.Result != null && probe.Result.Succeeded)
            {
                return probe.Result.StandardOutput;
            }

            warnings.Enqueue($"rustup {what}: {probe.Error ?? FirstLine(probe.Result?.StandardError) ?? "failed"}");
            return string.Empty;
        }

        return new RustupState
        {
            Show = ParseRustupShow(Output(await showTask.ConfigureAwait(false), "show")),
            Toolchains = ParseToolchainList(Output(await toolchainsTask.ConfigureAwait(false), "toolchain list")),
            Targets = ParseLines(Output(await targetsTask.ConfigureAwait(false), "target list")),
            Components = ParseLines(Output(await componentsTask.ConfigureAwait(false), "component list")),
        };
    }

    private async Task<(ProcessRunResult Result, string Error)> RunProbeAsync(string path, string[] arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var request = new ProcessRunRequest(path)
        {
            WorkingDirectory = workingDirectory,
            Timeout = _options.ToolProbeTimeout,
        };
        foreach (var argument in arguments)
        {
            request.Arguments.Add(argument);
        }

        foreach (var pair in BuildProbeEnvironment(_options))
        {
            request.Environment[pair.Key] = pair.Value;
        }

        try
        {
            return (await ProcessRunner.RunAsync(request, cancellationToken).ConfigureAwait(false), null);
        }
        catch (RustToolsException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// The environment every probe runs with: the options' variables, CARGO_HOME / RUSTUP_HOME
    /// overrides, and <c>RUSTUP_AUTO_INSTALL=0</c> unless the options set it explicitly.
    /// </summary>
    internal static IDictionary<string, string> BuildProbeEnvironment(RustWorkbenchOptions options)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.CargoHome))
        {
            environment["CARGO_HOME"] = options.CargoHome;
        }

        if (!string.IsNullOrWhiteSpace(options.RustupHome))
        {
            environment["RUSTUP_HOME"] = options.RustupHome;
        }

        foreach (var pair in options.Environment)
        {
            environment[pair.Key] = pair.Value;
        }

        if (!environment.ContainsKey("RUSTUP_AUTO_INSTALL"))
        {
            environment["RUSTUP_AUTO_INSTALL"] = "0";
        }

        return environment;
    }

    /// <summary>
    /// Parses <c>rustc -vV</c> output (<c>key: value</c> lines) into a dictionary keyed by the
    /// text before the first colon (<c>host</c>, <c>release</c>, <c>commit-hash</c>,
    /// <c>LLVM version</c>, ...).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseRustcVerbose(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(output))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                values[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
        }

        return values;
    }

    /// <summary>
    /// Parses <c>rustup show</c> output in both the current format (<c>name:</c> /
    /// <c>active because:</c> lines) and the older format (<c>toolchain (reason)</c> followed by
    /// a <c>rustc x.y.z</c> line).
    /// </summary>
    internal static RustupShowResult ParseRustupShow(string output)
    {
        var lines = SplitLines(output).ToList();
        var result = new RustupShowResult();
        var targets = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("Default host:", StringComparison.OrdinalIgnoreCase))
            {
                result.DefaultHost = line.Substring("Default host:".Length).Trim();
            }
            else if (line.StartsWith("rustup home:", StringComparison.OrdinalIgnoreCase))
            {
                result.RustupHome = line.Substring("rustup home:".Length).Trim();
            }
        }

        var headerIndex = lines.FindIndex(l => l.Trim().Equals("active toolchain", StringComparison.OrdinalIgnoreCase));
        if (headerIndex >= 0)
        {
            var inTargets = false;
            for (var i = headerIndex + 1; i < lines.Count; i++)
            {
                var raw = lines[i];
                var line = raw.Trim();
                if (line.Length == 0 || line.All(c => c == '-'))
                {
                    continue;
                }

                if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    result.ActiveToolchain = line.Substring("name:".Length).Trim();
                    inTargets = false;
                }
                else if (line.StartsWith("active because:", StringComparison.OrdinalIgnoreCase))
                {
                    result.ActiveReason = line.Substring("active because:".Length).Trim();
                    inTargets = false;
                }
                else if (line.StartsWith("installed targets:", StringComparison.OrdinalIgnoreCase))
                {
                    inTargets = true;
                }
                else if (inTargets && char.IsWhiteSpace(raw[0]))
                {
                    targets.Add(line);
                }
                else if (result.ActiveToolchain == null && !line.StartsWith("rustc ", StringComparison.Ordinal))
                {
                    (result.ActiveToolchain, result.ActiveReason) = SplitNameAndReason(line);
                }
                else
                {
                    inTargets = false;
                }
            }
        }
        else
        {
            var rustcIndex = lines.FindIndex(l => l.TrimStart().StartsWith("rustc ", StringComparison.Ordinal));
            for (var i = rustcIndex - 1; i >= 0; i--)
            {
                if (lines[i].Trim().Length > 0)
                {
                    (result.ActiveToolchain, result.ActiveReason) = SplitNameAndReason(lines[i].Trim());
                    break;
                }
            }
        }

        result.InstalledTargets = targets;
        return result;
    }

    /// <summary>
    /// Parses <c>rustup toolchain list</c> output; a <c>(default)</c> or <c>(active, default)</c>
    /// suffix marks the default toolchain.
    /// </summary>
    internal static IReadOnlyList<RustupToolchainEntry> ParseToolchainList(string output)
    {
        var entries = new List<RustupToolchainEntry>();
        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("no ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("info:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (name, reason) = SplitNameAndReason(trimmed);
            var markers = (reason ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            entries.Add(new RustupToolchainEntry(
                name,
                markers.Contains("default", StringComparer.OrdinalIgnoreCase),
                markers.Contains("active", StringComparer.OrdinalIgnoreCase)));
        }

        return entries;
    }

    /// <summary>
    /// Splits output into trimmed, non-empty lines, skipping rustup <c>info:</c> lines.
    /// </summary>
    internal static IReadOnlyList<string> ParseLines(string output)
        => SplitLines(output)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("info:", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static (string Name, string Reason) SplitNameAndReason(string line)
    {
        var open = line.IndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && line.EndsWith(')'))
        {
            return (line.Substring(0, open).Trim(), line.Substring(open + 2, line.Length - open - 3).Trim());
        }

        return (line.Trim(), null);
    }

    private static bool LooksNotInstalled(string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return false;
        }

        return standardError.Contains("is not installed", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("not installed for the toolchain", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("no default is configured", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("could not choose a version", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("unknown proxy name", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitLines(string text)
        => string.IsNullOrEmpty(text)
            ? Enumerable.Empty<string>()
            : text.Replace("\r\n", "\n").Split('\n');

    private static string FirstLine(string text)
        => SplitLines(text).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private sealed record RustcProbe(InstalledTool Tool, IReadOnlyDictionary<string, string> Values, string Sysroot = null);

    private sealed class RustupState
    {
        public RustupShowResult Show { get; init; } = new RustupShowResult();

        public IReadOnlyList<RustupToolchainEntry> Toolchains { get; init; } = Array.Empty<RustupToolchainEntry>();

        public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Components { get; init; } = Array.Empty<string>();
    }
}
