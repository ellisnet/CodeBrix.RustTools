using System;
using System.Collections.Concurrent;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// An <see cref="IProgress{T}"/> that records every report synchronously (unlike
/// <see cref="Progress{T}"/>, which posts to a synchronization context and may lag).
/// </summary>
internal sealed class CollectingProgress : IProgress<CargoProgress>
{
    public ConcurrentQueue<CargoProgress> Reports { get; } = new ConcurrentQueue<CargoProgress>();

    public void Report(CargoProgress value) => Reports.Enqueue(value);
}
