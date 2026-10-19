using System;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs its callback on the reporting thread (unlike
/// <see cref="Progress{T}"/>, which posts to a synchronization context), so tests see every
/// report before the awaited call returns.
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;
    private readonly object _gate = new object();

    public SynchronousProgress(Action<T> callback)
    {
        _callback = callback;
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            _callback(value);
        }
    }
}
