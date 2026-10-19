using System;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>An <see cref="IProgress{T}"/> that calls its handler on the reporting thread
/// (no synchronisation context), so cargo's reports arrive in order.</summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value)
    {
        try
        {
            _handler?.Invoke(value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A progress handler never breaks a build.
        }
    }
}
