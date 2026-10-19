using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.Docker;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The entry point: builds a Rust crate for a set of runtime identifiers and, optionally,
/// writes a .NET wrapper class library around it. Keep one instance for the life of the
/// application: it owns the host workbench (whose toolchain detection is cached) and the
/// Docker workbench (one Docker client), both created on first use.
/// </summary>
public sealed class CrateBuilder : IDisposable
{
    private readonly CrateBuilderStages _stages;
    private readonly Lazy<RustWorkbench> _hostWorkbench;
    private readonly Lazy<DockerRustWorkbench> _dockerWorkbench;
    private bool _disposed;

    /// <summary>
    /// Creates a builder. Nothing is detected or contacted until a method is called.
    /// </summary>
    /// <param name="options">The settings; <c>null</c> = the defaults.</param>
    public CrateBuilder(CrateBuilderOptions options = null)
        : this(options, CrateBuilderStages.CreateDefault())
    {
    }

    /// <summary>Creates a builder with replaced stages (tests).</summary>
    internal CrateBuilder(CrateBuilderOptions options, CrateBuilderStages stages)
    {
        Options = options ?? new CrateBuilderOptions();
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _stages.EnsureComplete();
        _hostWorkbench = new Lazy<RustWorkbench>(() => new RustWorkbench(Options.HostOptions), LazyThreadSafetyMode.ExecutionAndPublication);
        _dockerWorkbench = new Lazy<DockerRustWorkbench>(() => new DockerRustWorkbench(Options.DockerOptions), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The settings this builder uses.</summary>
    public CrateBuilderOptions Options { get; }

    /// <summary>
    /// Raised for every progress report of every build (on the thread that produced it;
    /// exceptions thrown by handlers are ignored).
    /// </summary>
    public event EventHandler<CrateBuildProgress> Progress;

    /// <summary>
    /// Checks whether this machine can build the request in its mode, and which of its
    /// targets: host mode detects the Rust toolchain (cargo, rustc, the C toolchain, installed
    /// targets, cross tools, the Android NDK, llvm-mingw, the macOS link profile); Docker mode
    /// checks the engine. Nothing is installed or built.
    /// </summary>
    /// <param name="request">The request (validated first).</param>
    /// <param name="cancellationToken">Cancels the detection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">The request is invalid (see
    /// <see cref="CrateBuildRequest.Validate"/>).</exception>
    public async Task<CrateReadinessReport> CheckReadinessAsync(CrateBuildRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var copy = request.Clone();
        var context = CreateContext(null);
        return await _stages.Readiness.CheckAsync(copy, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the crate: readiness, source, staging, the C facade when the crate needs one,
    /// cargo per target, verification, the wrapper (when asked for), the build record and
    /// cleanup - in that order. A stage that fails is reported in the result
    /// (<see cref="CrateBuildResult.FailedStage"/>), never thrown; targets this machine cannot
    /// build in the requested mode are reported as
    /// <see cref="CrateTargetOutcome.NotBuildableInThisMode"/> with install guidance while the
    /// others are built.
    /// </summary>
    /// <param name="request">The request (validated and copied first; the caller's object is
    /// never changed).</param>
    /// <param name="progress">Receives every progress report; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the build (the scratch folder is still cleaned
    /// up, then <see cref="OperationCanceledException"/> is thrown).</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">The request is invalid (see
    /// <see cref="CrateBuildRequest.Validate"/>).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was
    /// cancelled.</exception>
    public async Task<CrateBuildResult> BuildAsync(CrateBuildRequest request, IProgress<CrateBuildProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var copy = request.Clone();
        var context = CreateContext(progress);
        var pipeline = new CrateBuildPipeline(_stages);
        return await pipeline.RunAsync(copy, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the Docker workbench's client (when one was created).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_dockerWorkbench.IsValueCreated)
        {
            _dockerWorkbench.Value.Dispose();
        }
    }

    private CrateBuilderContext CreateContext(IProgress<CrateBuildProgress> progress)
    {
        var runDirectory = Path.Combine(
            Options.EffectiveScratchRootDirectory,
            "runs",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        return new CrateBuilderContext(Options, runDirectory, () => _hostWorkbench.Value, () => _dockerWorkbench.Value, report =>
        {
            try
            {
                progress?.Report(report);
            }
            catch (Exception)
            {
                // A caller's progress receiver must not break the build.
            }

            try
            {
                Progress?.Invoke(this, report);
            }
            catch (Exception)
            {
                // Handler exceptions are ignored, as documented.
            }
        });
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
