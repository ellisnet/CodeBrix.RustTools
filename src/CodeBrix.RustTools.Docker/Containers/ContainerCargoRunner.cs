using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Runs cargo inside a build container with <c>--message-format=json</c> and turns its output
/// into the same <see cref="CargoCommandResult"/> / <see cref="CargoTestResult"/> the host-side
/// <see cref="CargoRunner"/> produces: the argument list comes from the same builder and every
/// standard output and standard error line goes through the same collector. Paths in the
/// result (artifacts, diagnostics) are container paths.
/// </summary>
/// <remarks>
/// cargo runs as <c>setsid -w cargo ...</c> with a per-run marker variable in its environment,
/// which every process it starts (rustc, build scripts, linkers) inherits. On cancellation or
/// timeout every process carrying the marker is killed, so nothing keeps compiling in the
/// container. The container's main process does not reap orphans, so killed processes can stay
/// listed as zombie (<c>Z</c>) entries until the container is removed; they use no CPU or
/// memory.
/// </remarks>
internal static class ContainerCargoRunner
{
    /// <summary>The marker variable's name.</summary>
    public const string MarkerVariable = "CODEBRIX_RUSTTOOLS_RUN";

    /// <summary>The cargo target folder every container command uses: the target volume.</summary>
    public const string TargetDirectory = BuildContainer.TargetPath;

    /// <summary>The API level the Android targets are linked for (Android 7.0):
    /// <see cref="AndroidLinkProfile.DefaultApiLevel"/>.</summary>
    public const int AndroidApiLevel = AndroidLinkProfile.DefaultApiLevel;

    /// <summary>The folder of the NDK's LLVM tools inside the Android image.</summary>
    public const string AndroidNdkToolsDirectory = DockerfileGenerator.AndroidNdkHome + "/toolchains/llvm/prebuilt/linux-x86_64/bin";

    /// <summary>The cargo registry source folder inside the official <c>rust</c> image
    /// (<c>CARGO_HOME</c> is <c>/usr/local/cargo</c>; the registry lives on the cache volume),
    /// remapped by the macOS link profile so that it does not enter the binaries.</summary>
    public const string ContainerCargoRegistrySourceDirectory = "/usr/local/cargo/registry/src";

    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds the invocation for a command: cargo's arguments come from
    /// <see cref="CargoRunner.BuildArguments"/>, with <c>--target</c> set for the cross targets.
    /// </summary>
    /// <param name="command">The cargo subcommand.</param>
    /// <param name="options">The options (not modified).</param>
    /// <param name="target">The target to compile for. <see cref="CrossTarget.LinuxNative"/> and
    /// the Linux target of the engine's own architecture keep
    /// <see cref="CargoBuildOptions.TargetTriple"/> (normally unset: the container's own
    /// target); every other target replaces it with its triple and, for a Linux target of
    /// another architecture, adds the cross toolchain variables of
    /// <see cref="LinuxLinkProfile"/>.</param>
    /// <param name="workingDirectory">The container folder to run in.</param>
    /// <param name="nativeLinuxTriple">The engine's own Linux triple
    /// (<see cref="Engine.DockerEngineInfo.NativeLinuxTriple"/>), which decides whether a Linux
    /// target is native; <c>null</c> when unknown (then no Linux target is native and
    /// <see cref="CrossTarget.LinuxNative"/> cannot be used).</param>
    /// <exception cref="CrossTargetNotSupportedException"><see cref="CrossTarget.LinuxNative"/>
    /// with an unknown engine architecture.</exception>
    public static ContainerCargoInvocation CreateInvocation(CargoCommand command, CargoBuildOptions options, CrossTarget target, string workingDirectory, string nativeLinuxTriple)
    {
        var engine = new DockerEngineInfo { Architecture = ArchitectureOf(nativeLinuxTriple) };
        target = CrossTargetInfo.Resolve(target, engine);
        var info = CrossTargetInfo.Get(target);
        if (!info.IsSupported)
        {
            throw new CrossTargetNotSupportedException(target);
        }

        if (info.IsMacOSUniversal)
        {
            throw new ArgumentException("MacOSUniversal is not a cargo target: build MacOSArm64 and MacOSX64, then join them (BuildAsync does both when asked for MacOSUniversal).", nameof(target));
        }

        var effective = options?.Clone() ?? new CargoBuildOptions();
        var native = info.IsNativeOn(engine);
        if (!native)
        {
            effective.TargetTriple = info.Triple;
        }

        var arguments = CargoRunner.BuildArguments(command, effective, null);
        var marker = MarkerVariable + "=" + Guid.NewGuid().ToString("N");
        var environment = new List<string>
        {
            "CARGO_TERM_COLOR=never",
            "RUSTUP_AUTO_INSTALL=0",
            "CARGO_TARGET_DIR=" + TargetDirectory,
        };
        if (info.IsAndroid)
        {
            environment.AddRange(AndroidEnvironment(info));
        }

        if (info.IsMacOS)
        {
            environment.AddRange(MacOSEnvironment(info, effective.MacOSInstallName));
        }

        if (info.IsLinux && !native)
        {
            environment.AddRange(LinuxCrossEnvironment(info));
        }

        if (info.IsWindows)
        {
            environment.AddRange(WindowsEnvironment(info));
        }

        foreach (var pair in effective.Environment)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            {
                environment.Add(pair.Key + "=" + pair.Value);
            }
        }

        environment.Add(marker);
        var commandLine = new List<string> { "setsid", "-w", "cargo" };
        commandLine.AddRange(arguments);
        return new ContainerCargoInvocation
        {
            Command = command,
            Target = target,
            CommandLine = commandLine,
            CargoArguments = arguments.ToList(),
            Environment = environment,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? BuildContainer.WorkDirectory : workingDirectory,
            MarkerEntry = marker,
        };
    }

    /// <summary>
    /// The variables that point cargo and the <c>cc</c> crate at the NDK's clang wrappers for
    /// <see cref="AndroidApiLevel"/>: the C and C++ compilers, the archiver and ranlib for build
    /// scripts, and cargo's linker and archiver for the target. These are what
    /// <c>cargo ndk</c> sets; cargo is run directly because <c>cargo ndk</c> does not pass
    /// cargo's JSON messages through.
    /// </summary>
    public static IReadOnlyList<string> AndroidEnvironment(CrossTargetInfo info)
        => ContainerAndroidLinkProfile.EnvironmentFor(info.Triple).Select(pair => pair.Key + "=" + pair.Value).ToList();

    /// <summary>
    /// The link profile of the Windows targets inside the image: llvm-mingw at
    /// <see cref="DockerfileGenerator.LlvmMingwHome"/>. The same <see cref="WindowsGnullvmLinkProfile"/>
    /// type serves host builds, so a container and a host set identical variables.
    /// </summary>
    public static WindowsGnullvmLinkProfile ContainerWindowsLinkProfile { get; } = new WindowsGnullvmLinkProfile(DockerfileGenerator.LlvmMingwHome + "/bin");

    /// <summary>
    /// The variables that point cargo and the <c>cc</c> crate at llvm-mingw for a Windows target
    /// (<see cref="ContainerWindowsLinkProfile"/>), including the rustflags that link the
    /// unwinder statically.
    /// </summary>
    /// <param name="info">A Windows target.</param>
    public static IReadOnlyList<string> WindowsEnvironment(CrossTargetInfo info)
        => ContainerWindowsLinkProfile.EnvironmentFor(info.Triple).Select(pair => pair.Key + "=" + pair.Value).ToList();

    /// <summary>
    /// The variables that point cargo and the <c>cc</c> crate at the Debian cross toolchain of a
    /// Linux target of another architecture (<see cref="LinuxLinkProfile"/>, tools on PATH).
    /// </summary>
    /// <param name="info">A concrete Linux target.</param>
    public static IReadOnlyList<string> LinuxCrossEnvironment(CrossTargetInfo info)
        => LinuxLinkProfile.ForTriple(info.Triple).EnvironmentFor(info.Triple).Select(pair => pair.Key + "=" + pair.Value).ToList();

    /// <summary>The engine architecture name a native Linux triple implies, for resolving
    /// <see cref="CrossTarget.LinuxNative"/>; <c>null</c> for an unknown triple.</summary>
    private static string ArchitectureOf(string nativeLinuxTriple) => nativeLinuxTriple switch
    {
        "x86_64-unknown-linux-gnu" => "x86_64",
        "aarch64-unknown-linux-gnu" => "aarch64",
        "riscv64gc-unknown-linux-gnu" => "riscv64",
        _ => null,
    };

    /// <summary>
    /// The link profile of the Android targets inside the image: the NDK's toolchain at
    /// <see cref="AndroidNdkToolsDirectory"/>, API level <see cref="AndroidApiLevel"/>. The same
    /// <see cref="AndroidLinkProfile"/> type serves host builds, so a container and a host set
    /// identical variables.
    /// </summary>
    public static AndroidLinkProfile ContainerAndroidLinkProfile { get; } = new AndroidLinkProfile(AndroidNdkToolsDirectory, AndroidApiLevel);

    /// <summary>
    /// The link profile of the macOS targets inside the image: the image's <c>rust-lld</c>
    /// (<see cref="DockerfileGenerator.MacOSRustLldPath"/>), the derived sysroot
    /// (<see cref="DockerfileGenerator.MacOSSysrootDirectory"/>) and the container's cargo
    /// registry folder (<see cref="ContainerCargoRegistrySourceDirectory"/>).
    /// </summary>
    public static MacOSLinkProfile ContainerMacOSLinkProfile { get; } = new MacOSLinkProfile(
        DockerfileGenerator.MacOSRustLldPath,
        DockerfileGenerator.MacOSSysrootDirectory,
        ContainerCargoRegistrySourceDirectory);

    /// <summary>
    /// The variables that apply <see cref="ContainerMacOSLinkProfile"/> to a macOS target: the
    /// per-target linker and rustflags, <c>SDKROOT</c> and <c>MACOSX_DEPLOYMENT_TARGET</c>.
    /// </summary>
    /// <param name="info">A macOS target.</param>
    /// <param name="installName">The cdylib install name
    /// (<see cref="CargoBuildOptions.MacOSInstallName"/>), or <c>null</c>.</param>
    public static IReadOnlyList<string> MacOSEnvironment(CrossTargetInfo info, string installName)
        => ContainerMacOSLinkProfile.EnvironmentFor(info.Triple, installName).Select(pair => pair.Key + "=" + pair.Value).ToList();

    /// <summary>
    /// Runs the invocation and collects the result.
    /// </summary>
    /// <param name="client">The Docker client.</param>
    /// <param name="containerId">The build container.</param>
    /// <param name="invocation">What to run.</param>
    /// <param name="options">The options the invocation was made from (timeout, cancellation
    /// behaviour, raw output, expected crate count).</param>
    /// <param name="defaultTimeout">The limit used when <see cref="CargoBuildOptions.Timeout"/>
    /// is unset; <see cref="Timeout.InfiniteTimeSpan"/> for none.</param>
    /// <param name="report">Receives every progress report; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the run when cancelled.</param>
    public static async Task<CargoCommandResult> RunAsync(
        DockerClient client,
        string containerId,
        ContainerCargoInvocation invocation,
        CargoBuildOptions options,
        TimeSpan defaultTimeout,
        Action<CargoProgress> report,
        CancellationToken cancellationToken)
    {
        options ??= new CargoBuildOptions();
        var collector = new CargoOutputCollector(invocation.Command, options, report);
        var standardError = new StringBuilder();
        var stderrLock = new object();
        void OnStandardError(string line)
        {
            lock (stderrLock)
            {
                if (standardError.Length > 0)
                {
                    standardError.Append('\n');
                }

                standardError.Append(line);
            }

            collector.OnStandardErrorLine(line);
        }

        var limit = options.Timeout ?? defaultTimeout;
        using var timeoutSource = limit > TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var stopwatch = Stopwatch.StartNew();
        int exitCode;
        var timedOut = false;
        var cancelled = false;
        try
        {
            var run = await ContainerCommandRunner.RunStreamingAsync(
                client,
                containerId,
                invocation.CommandLine,
                invocation.WorkingDirectory,
                invocation.Environment,
                collector.OnStandardOutputLine,
                OnStandardError,
                linked.Token).ConfigureAwait(false);
            exitCode = (int)run.ExitCode;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            await KillAsync(client, containerId, invocation.MarkerEntry).ConfigureAwait(false);
            timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            if (!timedOut && options.ThrowOnCancellation)
            {
                throw new OperationCanceledException($"{invocation.DisplayCommandLine} was cancelled; its processes in the container were killed.", cancellationToken);
            }

            cancelled = !timedOut;
            exitCode = -1;
        }

        string text;
        lock (stderrLock)
        {
            text = standardError.ToString();
        }

        return collector.CreateResult(exitCode, timedOut, cancelled, stopwatch.Elapsed, invocation.DisplayCommandLine, text, invocation.WorkingDirectory);
    }

    /// <summary>
    /// Kills every process in the container whose environment carries the marker entry (cargo,
    /// rustc, build scripts, linkers), repeating until none is left. Best effort: failures are
    /// swallowed, because the run is already being abandoned.
    /// </summary>
    internal static async Task KillAsync(DockerClient client, string containerId, string markerEntry)
    {
        using var timeout = new CancellationTokenSource(KillTimeout);
        try
        {
            await ContainerCommandRunner.RunAsync(client, containerId, KillCommand(markerEntry), "/", null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DockerException || ex is OperationCanceledException || ex is ObjectDisposedException)
        {
            // Best effort; removing the container stops everything in it anyway.
        }
    }

    /// <summary>
    /// The shell command that finds the marked processes through <c>/proc/*/environ</c> and kills
    /// them with SIGKILL, in up to 25 rounds (a process can start a child while it is being
    /// killed).
    /// </summary>
    internal static IReadOnlyList<string> KillCommand(string markerEntry)
    {
        const string script =
            "m=\"$1\"; i=0; while [ $i -lt 25 ]; do found=; "
            + "for d in /proc/[0-9]*; do p=${d#/proc/}; [ \"$p\" = \"$$\" ] && continue; "
            + "if tr '\\0' '\\n' < \"$d/environ\" 2>/dev/null | grep -qxF \"$m\"; then kill -KILL \"$p\" 2>/dev/null && found=1; fi; done; "
            + "[ -z \"$found\" ] && exit 0; i=$((i+1)); sleep 0.2; done; exit 0";
        return new[] { "sh", "-c", script, "codebrix-kill", markerEntry };
    }
}
